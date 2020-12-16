// Copyright (c) Microsoft Corporation.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

namespace Microsoft.DotNet.DependencyManager

open System
open System.IO
open System.Reflection
open Internal.Utilities.FSharpEnvironment

/// Signature for ResolutionProbe callback
/// host implements this, it's job is to return a list of assembly paths to probe.
type AssemblyResolutionProbe = delegate of Unit -> seq<string>

/// Type that encapsulates AssemblyResolveHandler for managed packages
//type AssemblyResolveHandlerCoreclr (assemblyProbingPaths: AssemblyResolutionProbe) =

//    let coreclrContext = CoreClrAssemblyLoadContext()

//    let resolveAssemblyNetStandard (ctxt: obj (*AssemblyLoadContext*)) (assemblyName: AssemblyName): Assembly =

//        let assemblyPaths =
//            match assemblyProbingPaths with
//            | null -> Seq.empty<string>
//            | _ ->  assemblyProbingPaths.Invoke()

//        try
//            // args.Name is a displayname formatted assembly version.
//            // E.g:  "System.IO.FileSystem, Version=4.1.1.0, Culture=en-US, PublicKeyToken=b03f5f7f11d50a3a"
//            let simpleName = assemblyName.Name
//            let assemblyPathOpt = assemblyPaths |> Seq.tryFind(fun path -> Path.GetFileNameWithoutExtension(path) = simpleName)
//            match assemblyPathOpt with
//            | Some path ->
//                //coreclrContext.LoadFromAssemblyPath ctxt path
//                Assembly.LoadFrom(path)
//            | None -> Unchecked.defaultof<Assembly>

//        with | _ -> Unchecked.defaultof<Assembly>

//    let handler = Func<obj, AssemblyName, Assembly>(resolveAssemblyNetStandard)

//    do
//        //CoreClrAssemblyLoadContext.Default.AddResolvingHandler(handler)
//        ()

//    interface IDisposable with
//        member _x.Dispose() =
//            //CoreClrAssemblyLoadContext.Default.RemoveResolvingHandler(handler)
//            ()

/// Type that encapsulates AssemblyResolveHandler for managed packages
type AssemblyResolveHandlerDeskTop (assemblyProbingPaths: AssemblyResolutionProbe) =

    let resolveAssemblyNET (assemblyName: AssemblyName): Assembly =

        let loadAssembly assemblyPath =
            Assembly.LoadFrom(assemblyPath)

        let assemblyPaths =
            match assemblyProbingPaths with
            | null -> Seq.empty<string>
            | _ ->  assemblyProbingPaths.Invoke()

        try
            // args.Name is a displayname formatted assembly version.
            // E.g:  "System.IO.FileSystem, Version=4.1.1.0, Culture=en-US, PublicKeyToken=b03f5f7f11d50a3a"
            let simpleName = assemblyName.Name
            let assemblyPathOpt = assemblyPaths |> Seq.tryFind(fun path -> Path.GetFileNameWithoutExtension(path) = simpleName)
            match assemblyPathOpt with
            | Some path ->
                loadAssembly path
            | None -> Unchecked.defaultof<Assembly>

        with | _ -> Unchecked.defaultof<Assembly>

    let handler = new ResolveEventHandler(fun _ (args: ResolveEventArgs) -> resolveAssemblyNET (new AssemblyName(args.Name)))
    do AppDomain.CurrentDomain.add_AssemblyResolve(handler)

    interface IDisposable with
        member _x.Dispose() =
            AppDomain.CurrentDomain.remove_AssemblyResolve(handler)

type AssemblyResolveHandler (assemblyProbingPaths: AssemblyResolutionProbe) =

    let handler =
        if isRunningOnCoreClr then
            //new AssemblyResolveHandlerCoreclr(assemblyProbingPaths) :> IDisposable
            new AssemblyResolveHandlerDeskTop(assemblyProbingPaths) :> IDisposable
        else
            new AssemblyResolveHandlerDeskTop(assemblyProbingPaths) :> IDisposable

    interface IDisposable with
        member _.Dispose() = handler.Dispose()
