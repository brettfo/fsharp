// Copyright (c) Microsoft Corporation.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

namespace Microsoft.DotNet.DependencyManager

open System
open System.Reflection
open System.Reflection.Emit
open System.Linq.Expressions

[<AllowNullLiteral>]
type IDefaultCoreClrAssemblyLoadContext =
    //abstract AddResolvingHandler: handler: Func<obj, AssemblyName, Assembly> -> unit
    //abstract RemoveResolvingHandler: handler: Func<obj, AssemblyName, Assembly> -> unit
    abstract AddResolvingUnmanagedDllHandler: handler: Func<Assembly, string, IntPtr> -> unit
    abstract RemoveResolvingUnmanagedDllHandler: handler: Func<Assembly, string, IntPtr> -> unit

/// This type is the biggest hack to ever exist, and I'm truly sorry to those I may have hurt.
///
/// The type `System.Runtime.Loader.AssemblyLoadContext` from the NuGet package `System.Runtime.Loader`
/// doesn't exist in `netstandard2.0`, but will possibly exist at runtime.  This is a wrapper to hide
/// as much reflection magic as possible, and thus, my shame.
type CoreClrAssemblyLoadContext() =
    static let assemblyLoadContextType = typeof<obj>.Assembly.GetType("System.Runtime.Loader.AssemblyLoadContext")
    static let defaultAssemblyLoadContext =
        if isNull assemblyLoadContextType then null
        else
            let propertyInfo = assemblyLoadContextType.GetProperty("Default")
            propertyInfo.GetValue(null)
    //static let resolvingHandlerFuncType = // typeof<Func<AssemblyLoadContext, AssemblyName, Assembly>>
        //typeof<Func<obj, AssemblyName, Assembly>>.GetGenericTypeDefinition().MakeGenericType(assemblyLoadContextType, typeof<AssemblyName>, typeof<Assembly>)
    static let defaultAssemblyLoadContextForEvents =
        if isNull assemblyLoadContextType then null
        else
            //let resolvingEventInfo = assemblyLoadContextType.GetEvent("Resolving")
            let resolvingUnmanagedDllEventInfo = assemblyLoadContextType.GetEvent("ResolvingUnmanagedDll")
            { new IDefaultCoreClrAssemblyLoadContext with
                //member _.AddResolvingHandler (handler: Func<obj (*AssemblyLoadContext*), AssemblyName, Assembly>) =
                //    let p1 = Expression.Parameter(assemblyLoadContextType)
                //    let p2 = Expression.Parameter(typeof<AssemblyName>)
                //    //Expression.me
                //    let e: Expression = null
                //    //Expression<Func<obj, AssemblyName, Assembly>>
                //    let lambda = Expression.Lambda(e, p1, p2)
                //    let del = lambda.Compile()
                //    resolvingEventInfo.AddEventHandler(defaultAssemblyLoadContext, del)




                //    let eventHandlerType = resolvingEventInfo.EventHandlerType // typeof<Func<AssemblyLoadContext, AssemblyName, Assembly>>
                //    let addMethod = assemblyLoadContextType.GetMethod("add_Resolving")
                //    let _res = addMethod.Invoke(defaultAssemblyLoadContext, [| handler |])
                //    //let func = Activator.CreateInstance(resolvingHandlerFuncType, fun context name -> handler.Invoke(context, name))
                //    let _func = Activator.CreateInstance(eventHandlerType)
                //    //let ctor = resolvingHandlerFuncType.GetConstructors().[0]
                //    //let func = ctor.Invoke([| fun context name -> handler.Invoke(context, name) |])
                //    let func = handler :> Delegate
                //    resolvingEventInfo.AddEventHandler(defaultAssemblyLoadContext, func)
                //member _.RemoveResolvingHandler (handler: Func<obj (*AssemblyLoadContext*), AssemblyName, Assembly>) = resolvingEventInfo.RemoveEventHandler(defaultAssemblyLoadContext, handler)
                member _.AddResolvingUnmanagedDllHandler (handler: Func<Assembly, string, IntPtr>) = resolvingUnmanagedDllEventInfo.AddEventHandler(defaultAssemblyLoadContext, handler)
                member _.RemoveResolvingUnmanagedDllHandler (handler: Func<Assembly, string, IntPtr>) = resolvingUnmanagedDllEventInfo.RemoveEventHandler(defaultAssemblyLoadContext, handler) }

    static let loaderFunction =
        // Ideally we'd do this:
        //
        // type NativeAssemblyLoadContext() =
        //     AssemblyLoadContext()
        //     override _.Load(_path: AssemblyName): Assembly = raise (NotImplementedException())
        //     member _.CustomLoad(path: string): IntPtr = base.LoadUnmanagedDllFromPath(path)
        //
        // but since these types aren't necessarily available, we have to jump through some hoops.
        if isNull assemblyLoadContextType then
            fun _path -> IntPtr.Zero
        else
            let nativeLibraryType = typeof<obj>.Assembly.GetType("System.Runtime.InteropServices.NativeLibrary")
            let tryLoadMethod = nativeLibraryType.GetMethod("TryLoad", [| typeof<string>; typeof<IntPtr>.MakeByRefType() |])
            fun (path: string) ->
                let parameters = [| box path; null |]
                let success = tryLoadMethod.Invoke(null, parameters) :?> bool
                match success with
                | true -> parameters.[1] :?> IntPtr
                | false -> IntPtr.Zero


            //let assemblyName = sprintf "asm_%s" (Guid.NewGuid().ToString("N")) |> AssemblyName
            //let assemblyBuilderParent = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run)
            //let moduleBuilderParent = assemblyBuilderParent.DefineDynamicModule("mod")
            //let typeBuilderParent = moduleBuilderParent.DefineType("HACK_AssemblyBuilder", TypeAttributes.Class, typeof<AssemblyBuilder>)
            //let methodBuilder = typeBuilderParent.DefineMethod("get_Location", MethodAttributes.Assembly, typeof<string>, [||])
            //let ilGen = methodBuilder.GetILGenerator()
            //ilGen.Emit(OpCodes.Ldstr, typeof<CoreClrAssemblyLoadContext>.Assembly.Location)
            //ilGen.Emit(OpCodes.Ret)
            //typeBuilderParent.DefineMethodOverride(methodBuilder, typeof<AssemblyBuilder>.GetMethod("get_Location"))
            //let assemblyBuilderTypeInfo = typeBuilderParent.CreateTypeInfo()
            //let assemblyBuilderType = assemblyBuilderTypeInfo.AsType()
            
            //let assemblyBuilder = Activator.CreateInstance(assemblyBuilderType) :?> AssemblyBuilder
            //let moduleBuilder = assemblyBuilder.DefineDynamicModule("mod2")
            //let typeBuilder = moduleBuilder.DefineType("NativeAssemblyLoadContext", TypeAttributes.Class, assemblyLoadContextType)
            //let typeInfo = typeBuilder.CreateTypeInfo()
            //let newType = typeInfo.AsType()
            //let nativeLoadContext = Activator.CreateInstance(newType)
            //let loadUnmanagedDllMethod = assemblyLoadContextType.GetMethod("LoadUnmanagedDllFromPath", BindingFlags.NonPublic ||| BindingFlags.Instance)
            //fun (path: string) -> loadUnmanagedDllMethod.Invoke(nativeLoadContext, [| box path |]) :?> IntPtr

    member _.LoadUnmanagedDllFromPath (path: string): IntPtr = loaderFunction path

    member _.LoadFromAssemblyPath (context: obj (*AssemblyLoadContext*)) (path: string) =
        let loadFromAssemblyPathMethod = assemblyLoadContextType.GetMethod("LoadFromAssemblyPath")
        let assembly = loadFromAssemblyPathMethod.Invoke(context, [| box path |]) :?> Assembly
        assembly

    static member Default = defaultAssemblyLoadContextForEvents
