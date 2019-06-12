// Copyright (c) Microsoft Corporation.  All Rights Reserved.  See License.txt in the project root for license information.

namespace FSharp.Compiler.LanguageServer

open System
open System.Collections.Concurrent
open System.IO
open FSharp.Compiler.SourceCodeServices
open Microsoft.Build.Construction
open Microsoft.Build.Definition
open Microsoft.Build.Evaluation
open StreamJsonRpc

type State() =

    let checker = FSharpChecker.Create()

    let sourceFileToProjectMap = ConcurrentDictionary<string, FSharpProjectOptions>()

    let shutdownEvent = new Event<_>()
    let exitEvent = new Event<_>()
    let cancelEvent = new Event<_>()
    let projectInvalidatedEvent = new Event<_>()

    let fileChanged (args: FileSystemEventArgs) =
        match sourceFileToProjectMap.TryGetValue args.FullPath with
        | true, projectOptions -> projectInvalidatedEvent.Trigger(projectOptions)
        | false, _ -> ()
    let fileRenamed (args: RenamedEventArgs) =
        match sourceFileToProjectMap.TryGetValue args.FullPath with
        | true, projectOptions -> projectInvalidatedEvent.Trigger(projectOptions)
        | false, _ -> ()
    let fileWatcher = new FileSystemWatcher()
    do fileWatcher.IncludeSubdirectories <- true
    do fileWatcher.Changed.Add(fileChanged)
    do fileWatcher.Created.Add(fileChanged)
    do fileWatcher.Deleted.Add(fileChanged)
    do fileWatcher.Renamed.Add(fileRenamed)

    let getProjectOptions (rootDir: string) =
        if isNull rootDir then [||]
        else
            fileWatcher.Path <- rootDir
            fileWatcher.EnableRaisingEvents <- true
            let getProjectSourceFiles (path: string) =
                let options = ProjectOptions()
                let project = Project.FromFile(path, options)
                let compileItems = project.GetItems("Compile")
                let sourceFiles =
                    compileItems
                    |> Seq.map (fun c -> c.GetMetadataValue("FullPath"))
                    |> Seq.toArray
                let projectOptions: FSharpProjectOptions =
                    { ProjectFileName = path
                      ProjectId = None
                      SourceFiles = sourceFiles
                      OtherOptions = [||]
                      ReferencedProjects = [||]
                      IsIncompleteTypeCheckEnvironment = false
                      UseScriptResolutionRules = false
                      LoadTime = DateTime.Now
                      UnresolvedReferences = None
                      OriginalLoadReferences = []
                      ExtraProjectInfo = None
                      Stamp = None }
                for sourceFile in sourceFiles do
                    sourceFileToProjectMap.AddOrUpdate(sourceFile, projectOptions, fun _ _ -> projectOptions) |> ignore
                projectOptions
            let topLevelProjects = Directory.GetFiles(rootDir, "*.fsproj")
            let watchableProjectPaths =
                match topLevelProjects with
                | [||] ->
                    match Directory.GetFiles(rootDir, "*.sln") with
                    // TODO: what to do with multiple .sln or a combo of .sln/.fsproj?
                    | [| singleSolution |] ->
                        let sln = SolutionFile.Parse(singleSolution)
                        let projects =
                            sln.ProjectsInOrder
                            |> Seq.map (fun p -> p.AbsolutePath)
                            |> Seq.filter (fun p -> Path.GetExtension(p) = ".fsproj")
                            |> Seq.toArray
                        projects
                    | _ -> [||]
                | _ -> topLevelProjects
            let watchableProjectOptions =
                watchableProjectPaths
                |> Array.map getProjectSourceFiles
            watchableProjectOptions

    member __.Checker = checker

    /// Initialize the LSP at the specified location.  According to the spec, `rootUri` is to be preferred over `rootPath`.
    member __.Initialize (rootPath: string) (rootUri: DocumentUri) (computeDiagnostics: FSharpProjectOptions -> unit) =
        let rootDir =
            if not (isNull rootUri) then Uri(rootUri).LocalPath
            else rootPath
        let projectOptions = getProjectOptions rootDir
        projectInvalidatedEvent.Publish.Add computeDiagnostics // compute diagnostics on project invalidation
        for projectOption in projectOptions do
            computeDiagnostics projectOption // compute initial set of diagnostics

    [<CLIEvent>]
    member __.Shutdown = shutdownEvent.Publish

    [<CLIEvent>]
    member __.Exit = exitEvent.Publish

    [<CLIEvent>]
    member __.Cancel = cancelEvent.Publish

    [<CLIEvent>]
    member __.ProjectInvalidated = projectInvalidatedEvent.Publish

    member __.DoShutdown() = shutdownEvent.Trigger()

    member __.DoExit() = exitEvent.Trigger()

    member __.DoCancel() = cancelEvent.Trigger()

    member __.InvalidateAllProjects() =
        for projectOptions in sourceFileToProjectMap.Values do
            projectInvalidatedEvent.Trigger(projectOptions)

    member val Options = Options.Default() with get, set

    member val JsonRpc: JsonRpc option = None with get, set
