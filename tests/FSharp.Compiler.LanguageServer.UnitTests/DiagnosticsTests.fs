// Copyright (c) Microsoft Corporation.  All Rights Reserved.  See License.txt in the project root for license information.

namespace FSharp.Compiler.LanguageServer.UnitTests

open System
open System.IO
open System.Linq
open System.Threading.Tasks
open FSharp.Compiler.LanguageServer
open Nerdbank.Streams
open NUnit.Framework

[<TestFixture>]
type DiagnosticsTests() =

    let createTestableProject (tfm: string) (sourceFiles: (string * string) list) =
        let testDir = new TemporaryDirectory()
        let directoryBuildText = "<Project />"
        File.WriteAllText(Path.Combine(testDir.Directory, "Directory.Build.props"), directoryBuildText)
        File.WriteAllText(Path.Combine(testDir.Directory, "Directory.Build.targets"), directoryBuildText)
        //File.WriteAllText(Path.Combine(testDir.Directory, "global.json"), "{\"sdk\":{\"version\": \"2.1.503\"}}")
        for name, contents in sourceFiles do
            File.WriteAllText(Path.Combine(testDir.Directory, name), contents)
        let compileItems =
            sourceFiles
            |> List.map fst
            |> List.map (sprintf "    <Compile Include=\"%s\" />")
            |> List.fold (fun content line -> content + "\n" + line) ""
        let replacements =
            [ "{{COMPILE}}", compileItems
              "{{TARGETFRAMEWORK}}", tfm ]
        let projectTemplate =
            @"
<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <OutputType>Library</OutputType>
    <TargetFramework>{{TARGETFRAMEWORK}}</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
{{COMPILE}}
  </ItemGroup>
</Project>"
        let projectFile =
            replacements
            |> List.fold (fun (content: string) (find, replace) -> content.Replace(find, replace)) projectTemplate
        File.WriteAllText(Path.Combine(testDir.Directory, "test.fsproj"), projectFile)
        testDir

    let createRpcClient (rootPath: string) =
        let clientStream, serverStream = FullDuplexStream.CreatePair().ToTuple()
        let server = new Server(serverStream, serverStream)
        server.StartListening()
        let client = new TestClient(rootPath, clientStream, clientStream, server)
        client

    let createClientTest (tfm: string) (sourceFiles: (string * string) list) =
        let testDir = createTestableProject tfm sourceFiles
        let client = createRpcClient testDir.Directory
        client

    let getDiagnostics (content: string) =
        async {
            let client = createClientTest "netstandard2.0" [ "Program.fs", content ]
            let! diagnostics = client.WaitForDiagnosticsAsync client.Initialize ["Program.fs"]
            return diagnostics.["Program.fs"]
        }

    [<Test>]
    member __.``No diagnostics for correct code``() =
        async {
            let! diagnostics = getDiagnostics @"
namespace Test

module Numbers =
    let one: int = 1
"
            Assert.AreEqual(0, diagnostics.Length)
        } |> Async.StartAsTask :> Task

    [<Test>]
    member __.``Diagnostics for incorrect code``() =
        async {
            let! diagnostics = getDiagnostics @"
namespace Test

module Numbers =
    let one: int = false
"
            let diag = diagnostics.Single()
            Assert.AreEqual("FS0001", diag.code)
            Assert.AreEqual(Some 1, diag.severity)
            Assert.AreEqual(4, diag.range.start.line)
            Assert.AreEqual(19, diag.range.start.character)
            Assert.AreEqual(4, diag.range.``end``.line)
            Assert.AreEqual(24, diag.range.``end``.character)
            Assert.AreEqual("This expression was expected to have type\n    'int'    \nbut here has type\n    'bool'", diag.message.Trim())
            Assert.IsTrue(diag.source.Value.EndsWith("Program.fs"))
        } |> Async.StartAsTask :> Task

    [<Test>]
    member __.``Diagnostics added for updated incorrect code``() =
        async {
            let correct = @"
namespace Test

module Numbers =
    let one: int = 1
"
            let incorrect = @"
namespace Test

module Numbers =
    let one: int = false
"

            // verify initial state
            let client = createClientTest "netstandard2.0" [ "Program.fs", correct ]
            let! diagnostics = client.WaitForDiagnosticsAsync client.Initialize ["Program.fs"]
            Assert.AreEqual(0, diagnostics.["Program.fs"].Length)

            // touch file with incorrect data
            let touch () = File.WriteAllText(Path.Combine(client.RootPath, "Program.fs"), incorrect)
            let! diagnostics = client.WaitForDiagnostics touch ["Program.fs"]
            let diag = diagnostics.["Program.fs"].Single()
            Assert.AreEqual("FS0001", diag.code)
        } |> Async.StartAsTask :> Task

    [<Test>]
    member __.``Diagnostics removed for updated correct code``() =
        async {
            let incorrect = @"
namespace Test

module Numbers =
    let one: int = false
"
            let correct = @"
namespace Test

module Numbers =
    let one: int = 1
"

            // verify initial state
            let client = createClientTest "netstandard2.0" [ "Program.fs", incorrect ]
            let! diagnostics = client.WaitForDiagnosticsAsync client.Initialize ["Program.fs"]
            let diag = diagnostics.["Program.fs"].Single()
            Assert.AreEqual("FS0001", diag.code)

            // touch file with incorrect data
            let touch () = File.WriteAllText(Path.Combine(client.RootPath, "Program.fs"), correct)
            let! diagnostics = client.WaitForDiagnostics touch ["Program.fs"]
            Assert.AreEqual(0, diagnostics.["Program.fs"].Length)
        } |> Async.StartAsTask :> Task
