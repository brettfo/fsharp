﻿// Copyright (c) Microsoft Corporation.  All Rights Reserved.  See License.txt in the project root for license information.

namespace FSharp.Compiler.LanguageServer.UnitTests

open System
open System.Collections.Generic
open System.Diagnostics
open System.IO
open System.Threading
open FSharp.Compiler.LanguageServer
open Newtonsoft.Json.Linq
open StreamJsonRpc

type TestClient(rootPath:string, sendingStream: Stream, receivingStream: Stream, server: Server) =

    let rootPath = if rootPath.EndsWith(Path.DirectorySeparatorChar.ToString()) then rootPath else rootPath + Path.DirectorySeparatorChar.ToString()
    let diagnosticsEvent = Event<_>()

    let formatter = JsonMessageFormatter()
    let converter = JsonOptionConverter() // special handler to convert between `Option<'T>` and `obj/null`.
    do formatter.JsonSerializer.Converters.Add(converter)
    let handler = new HeaderDelimitedMessageHandler(sendingStream, receivingStream, formatter)
    let client = new JsonRpc(handler)
    let handler (functionName: string) (args: JToken): JToken =
        match functionName with
        | TextDocumentPublishDiagnostics ->
            let args = args.ToObject<PublishDiagnosticsParams>(formatter.JsonSerializer)
            let fullPath = Uri(args.uri).LocalPath
            let shortPath = if fullPath.StartsWith(rootPath) then fullPath.Substring(rootPath.Length) else fullPath
            diagnosticsEvent.Trigger((shortPath, args.diagnostics))
            null
        | _ -> null
    let addHandler (name: string) =
        client.AddLocalRpcMethod(name, new Func<JToken, JToken>(handler name))
    do addHandler TextDocumentPublishDiagnostics
    do client.StartListening()

    member __.RootPath = rootPath

    member __.Server = server

    [<CLIEvent>]
    member __.PublishDiagnostics = diagnosticsEvent.Publish

    member __.Initialize () =
        async {
            do! client.NotifyWithParameterObjectAsync(OptionsSet, {| options = Options.AllOn() |}) |> Async.AwaitTask
            let capabilities: ClientCapabilities =
                { workspace = None
                  textDocument = None
                  experimental = None
                  supportsVisualStudioExtensions = None }
            let! _result =
                client.InvokeWithParameterObjectAsync<InitializeResult>(
                    "initialize", // method
                    {| processId = Process.GetCurrentProcess().Id
                       rootPath = rootPath
                       capabilities = capabilities |}
                    ) |> Async.AwaitTask
            return ()
        }

    member this.WaitForDiagnostics (triggerAction: unit -> unit) (fileNames: string list) =
        async {
            // prepare file diagnostic triggers
            let diagnosticTriggers = Dictionary<string, ManualResetEvent>()
            fileNames |> List.iter (fun f -> diagnosticTriggers.[f] <- new ManualResetEvent(false))

            // prepare callback handler
            let diagnosticsMap = Dictionary<string, Diagnostic[]>()
            let handler (fileName: string, diagnostics: Diagnostic[]) =
                diagnosticsMap.[fileName] <- diagnostics
                diagnosticTriggers.[fileName].Set() |> ignore

            // subscribe to the event
            let wrappedHandler = new Handler<string * Diagnostic[]>(fun _sender args -> handler args)
            this.PublishDiagnostics.AddHandler(wrappedHandler)
            triggerAction ()

            // wait for all triggers to hit
            let! results =
                diagnosticTriggers
                |> Seq.map (fun entry ->
                    async {
                        let! result = Async.AwaitWaitHandle(entry.Value, millisecondsTimeout = 5000)
                        return if result then None
                               else Some <| sprintf "No diagnostics received for file [%s]" entry.Key
                    })
                |> Async.Parallel
            let results = results |> Array.choose (fun x -> x)
            if results.Length > 0 then
                let combinedErrors = String.Join("\n", results)
                failwith <| sprintf "Error waiting for diagnostics:\n%s" combinedErrors

            // clean up event
            this.PublishDiagnostics.RemoveHandler(wrappedHandler)

            // done
            return diagnosticsMap
        }

    member this.WaitForDiagnosticsAsync (triggerAction: unit -> Async<unit>) (fileNames: string list) =
        this.WaitForDiagnostics (fun () -> triggerAction () |> Async.RunSynchronously) fileNames
