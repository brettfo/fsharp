// Copyright (c) Microsoft Corporation.  All Rights Reserved.  See License.txt in the project root for license information.

namespace FSharp.Compiler.LanguageServer

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Diagnostics
open System.IO
open FSharp.Compiler.SourceCodeServices
open Microsoft.Build.Construction
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

    let execProcess (name: string) (args: string) =
        let startInfo = ProcessStartInfo(name, args)
        startInfo.CreateNoWindow <- true
        startInfo.RedirectStandardOutput <- true
        startInfo.UseShellExecute <- false
        let lines = List<string>()
        use proc = new Process()
        proc.StartInfo <- startInfo
        proc.OutputDataReceived.Add(fun args -> lines.Add(args.Data))
        proc.Start() |> ignore
        proc.BeginOutputReadLine()
        proc.WaitForExit()
        lines.ToArray()

    let linesWithPrefixClean (prefix: string) (lines: string[]) =
        lines
        |> Array.filter (isNull >> not)
        |> Array.map (fun line -> line.TrimStart(' '))
        |> Array.filter (fun line -> line.StartsWith(prefix))
        |> Array.map (fun line -> line.Substring(prefix.Length))

    let getProjectOptions (rootDir: string) =
        if isNull rootDir then [||]
        else
            fileWatcher.Path <- rootDir
            fileWatcher.EnableRaisingEvents <- true
            let getProjectOptions (projectPath: string) =
                let projectDir = Path.GetDirectoryName(projectPath)
                let normalizePath (path: string) =
                    if Path.IsPathRooted(path) then path
                    else Path.Combine(projectDir, path)

                let customTargetsPath = Path.Combine(Path.GetDirectoryName(typeof<State>.Assembly.Location), "FSharp.Compiler.LanguageServer.DesignTime.targets")
                let detectedTfmSentinel = "DetectedTargetFramework="
                let detectedCommandLineArgSentinel = "DetectedCommandLineArg="

                // find the target frameworks
                let targetFrameworks =
                    sprintf "msbuild \"%s\" \"/p:CustomAfterMicrosoftCommonCrossTargetingTargets=%s\" \"/p:CustomAfterMicrosoftCommonTargets=%s\" /t:ReportTargetFrameworks" projectPath customTargetsPath customTargetsPath
                    |> execProcess "dotnet"
                    |> linesWithPrefixClean detectedTfmSentinel

                let getArgs (tfm: string) =
                    sprintf "build \"%s\" \"/p:CustomAfterMicrosoftCommonTargets=%s\" \"/p:TargetFramework=%s\" /t:Restore;ReportCommandLineArgs" projectPath customTargetsPath tfm
                    |> execProcess "dotnet"
                    |> linesWithPrefixClean detectedCommandLineArgSentinel

                let tfmAndArgs =
                    targetFrameworks
                    |> Array.map (fun tfm -> tfm, getArgs tfm)

                let separateArgs (args: string[]) =
                    args
                    |> Array.partition (fun a -> a.StartsWith("-"))
                    |> (fun (options, files) ->
                        let normalizedFiles = files |> Array.map normalizePath
                        options, normalizedFiles)

                // TODO: for now we're only concerned with the first TFM
                let _tfm, args = Array.head tfmAndArgs

                let otherOptions, sourceFiles = separateArgs args

                let projectOptions: FSharpProjectOptions =
                    { ProjectFileName = projectPath
                      ProjectId = None
                      SourceFiles = sourceFiles
                      OtherOptions = otherOptions
                      ReferencedProjects = [||] // TODO: populate from @(ProjectReference)
                      IsIncompleteTypeCheckEnvironment = false
                      UseScriptResolutionRules = false
                      LoadTime = DateTime.Now
                      UnresolvedReferences = None
                      OriginalLoadReferences = []
                      ExtraProjectInfo = None
                      Stamp = None }
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
                |> Array.map getProjectOptions

            // associate source files with project options
            // TODO: watch for changes to .fsproj
            // TODO: watch for changes to .deps.json
            watchableProjectOptions
            |> Seq.iter (fun projectOptions ->
                projectOptions.SourceFiles
                |> Seq.iter (fun sourceFile -> sourceFileToProjectMap.AddOrUpdate(sourceFile, projectOptions, fun _ _ -> projectOptions) |> ignore ))

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
