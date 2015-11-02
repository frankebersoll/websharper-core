// $begin{copyright}
//
// This file is part of WebSharper
//
// Copyright (c) 2008-2014 IntelliFactory
//
// Licensed under the Apache License, Version 2.0 (the "License"); you
// may not use this file except in compliance with the License.  You may
// obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or
// implied.  See the License for the specific language governing
// permissions and limitations under the License.
//
// $end{copyright}

/// The main entry-point module of WebSharper.
module WebSharper.FSharp.Program

open System
open System.IO
open System.Reflection
open IntelliFactory.Core
open WebSharper
open WebSharper.Compiler

type ProjectType =
    | Bundle
    | WebSite
    | Html
    | WIG

type WsConfig =
    {
        SourceMap   : bool
        TypeScript  : bool
        ProjectType : ProjectType option
        OutputPath  : string option
        AssemblyFile : string
        References  : string[] 
        FscPath     : string
        FscArgs     : string[]        
        ProjectFile : string
        Documentation : string option
        VSStyleErrors : bool
        PrintJS : bool
    }

    static member Empty =
        {                 
             SourceMap   = false
             TypeScript  = false
             ProjectType = None
             OutputPath  = None
             AssemblyFile = null
             References  = [||]
             FscPath     = null
             FscArgs     = [||]
             ProjectFile = null
             Documentation = None
             VSStyleErrors = false
             PrintJS  = false
        }
    
let LoadInterfaceGeneratorAssembly (aR: AssemblyResolver) (file: string) =
        let asm = Assembly.Load(File.ReadAllBytes(file))
        let name = AssemblyName.GetAssemblyName(file)
        match Attribute.GetCustomAttribute(asm, typeof<InterfaceGenerator.Pervasives.ExtensionAttribute>) with
        | :? InterfaceGenerator.Pervasives.ExtensionAttribute as attr ->
            name, attr.GetAssembly(), asm
        | _ ->
            failwith "No ExtensionAttribute set on the input assembly"

let logf x = 
    Printf.kprintf (fun s -> File.AppendAllLines(@"D:\wsfscruns.txt", [s])) x

let RunInterfaceGenerator aR snk config =
        let (name, asmDef, asm) = LoadInterfaceGeneratorAssembly aR config.AssemblyFile
        let cfg =
            {
                InterfaceGenerator.CompilerOptions.Default(name.Name) with
                    AssemblyResolver = Some aR
                    AssemblyVersion = name.Version
                    DocPath = None //input.DocumentationFile
                    EmbeddedResources = [] //input.EmbeddedResources
                    ProjectDir = Path.GetDirectoryName(config.ProjectFile) //input.ProjectDir
                    ReferencePaths = config.References //input.References
                    StrongNameKeyPair = snk
            }

        let cmp = InterfaceGenerator.Compiler.Create()
        let out = cmp.Compile(cfg, asmDef, asm)
        out.Save config.AssemblyFile
        let assem = Mono.Cecil.AssemblyDefinition.ReadAssembly config.AssemblyFile
        let meta =
            WebSharper.Compiler.Reflector.transformAssembly assem
//        let methodNames = comp.Classes.Values |> Seq.collect (fun c -> c.Methods.Keys |> Seq.map (fun m -> m.Value.MethodName)) |> Array.ofSeq
        WebSharper.Compiler.FrontEnd.modifyAssembly WebSharper.Core.Metadata.empty meta assem |> ignore
        assem.Write config.AssemblyFile

let CompileFSharp config =
    use proc =
        new System.Diagnostics.Process(
            StartInfo = 
                System.Diagnostics.ProcessStartInfo(
                    config.FscPath,
                    config.FscArgs |> Seq.map (fun a -> "\"" + a + "\"") |> String.concat " ",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                )
        )
//    proc.OutputDataReceived.Add(fun e ->
//        printfn "%s" e.Data
//    )
//    proc.ErrorDataReceived.Add(fun e ->
//        eprintfn "%s" e.Data
//    )
    Path.GetDirectoryName config.AssemblyFile |> Directory.CreateDirectory |> ignore
    proc.Start() |> ignore
    proc.WaitForExit()
    proc.StandardOutput.ReadToEnd() |> printfn "%s"
    let errors = proc.StandardError.ReadToEnd() 
    if not (String.IsNullOrEmpty errors) then
        logf "F# Errors:"
        logf "%s" errors
    eprintfn "%s" errors
    if proc.ExitCode <> 0 then
        Environment.Exit proc.ExitCode
//        failwith "F# compilation error"   

let Compile config =
    let started = System.DateTime.Now
    
    CompileFSharp config
    
    let ended = System.DateTime.Now
    logf "F# compilation (with fsc.exe): %A" (ended - started)
    let started = ended 
    
    //compiler.CompileFSharp(config.FscArgs, config.AssemblyFile)
    if config.VSStyleErrors then () else
    if config.ProjectFile = null then
        failwith "You must provide project file path."
    if config.AssemblyFile = null then
        failwith "You must provide assembly output path."
    let objPath = Path.Combine(Path.GetDirectoryName config.ProjectFile, config.AssemblyFile)
    let origPath = Path.ChangeExtension(objPath, ".orig" + Path.GetExtension objPath)
    File.Copy(objPath, origPath, true)
    let paths =
        [
            for r in config.References -> Path.GetFullPath r
            yield origPath 
        ]        
    let aR =
        AssemblyResolution.AssemblyResolver.Create()
            .SearchPaths(paths)
    aR.Wrap <| fun () ->
//    let t2 = System.Type.GetType("WebSharper.Macro+LT, WebSharper.Main")
    if config.ProjectType = Some WIG then  
        RunInterfaceGenerator aR None config // snk

        let ended = System.DateTime.Now
        logf "WIG running time: %A" (ended - started)

    else    
    let loader = WebSharper.Compiler.FrontEnd.Loader.Create aR (eprintfn "%s") //(fun msg -> out.Add(CompilerMessage.Warn msg))
    let refs = [ for r in config.References -> loader.LoadFile(r) ]
    let refMeta =
        let metas = refs |> List.choose (fun r -> WebSharper.Compiler.FrontEnd.readFromAssembly r)
        if List.isEmpty metas then None else Some (WebSharper.Core.Metadata.union metas)

    let ended = System.DateTime.Now
    logf "Loading referenced metadata: %A" (ended - started)
    let started = ended 

    let compiler = WebSharper.Compiler.FSharp.WebSharperFSharpCompiler(logf "%s")

    let ended = System.DateTime.Now
    logf "Initializing compiler: %A" (ended - started)
    let started = ended 
//
//    let classNames =
//        refMeta|> Option.map (fun m -> m.Classes |> Seq.map (fun c -> c.Key.Value.AssemblyQualifiedName) |> List.ofSeq)

    let assemblyResolveHandler = ResolveEventHandler(fun _ a ->
        let withoutExtAndOrig p =
            let n = Path.GetFileNameWithoutExtension p
            if n.EndsWith ".orig" then n.[ .. n.Length - 6] else n
        let r =
            paths |> List.tryFind (fun p -> withoutExtAndOrig p = a.Name)
        match r with
        | Some r -> System.Reflection.Assembly.LoadFile r
        | _ -> null
        )

    System.AppDomain.CurrentDomain.add_AssemblyResolve(assemblyResolveHandler)
    
    let comp =
        compiler.CompileWithArgs(refMeta, config.FscArgs, config.ProjectFile)

    System.AppDomain.CurrentDomain.remove_AssemblyResolve(assemblyResolveHandler)

    let ended = System.DateTime.Now
    logf "WebSharper compilation full: %A" (ended - started)
    let started = ended 

    let thisMeta = comp.ToCurrentMetadata()
    let merged = 
        WebSharper.Core.Metadata.union 
            [
                (match refMeta with Some m -> m | _ -> WebSharper.Core.Metadata.empty)
                thisMeta
            ]

    let assem = Mono.Cecil.AssemblyDefinition.ReadAssembly config.AssemblyFile
    let js = WebSharper.Compiler.FrontEnd.modifyAssembly merged thisMeta assem
            
    if config.PrintJS then
        match js with 
        | Some js ->
            printfn "%s" js
            logf "%s" js
        | _ -> ()

//    let rec tryWrite attempt =
//        if attempt = 10 then
//            assem.Write config.AssemblyFile
//        else
//            try assem.Write config.AssemblyFile
//            with _ ->
//                System.Threading.Thread.Sleep 200
//                tryWrite (attempt + 1)
//
//    tryWrite 0

    assem.Write config.AssemblyFile

    let ended = System.DateTime.Now
    logf "Serializing and writing metadata: %A" (ended - started)

//    printfn "WebSharper metadata written"

let (|StartsWith|_|) start (input: string) =    
    if input.StartsWith start then
        Some input.[start.Length ..]
    else None 

[<EntryPoint>]
let main argv =

    logf "%s" Environment.CommandLine            
    logf "Started at: %A" System.DateTime.Now

    let wsArgs = ref WsConfig.Empty
    let refs = ResizeArray()
    let fscArgs = ResizeArray()

    for a in argv do
        let setProjectType t =
            match (!wsArgs).ProjectType with
            | None -> wsArgs := { !wsArgs with ProjectType = Some t }
            | _ -> failwith "Conflicting WebSharper project types set."
        match a with
        | "--jsmap" -> wsArgs := { !wsArgs with SourceMap = true } 
        | "--dts" -> wsArgs := { !wsArgs with TypeScript = true } 
        | "--wig" -> setProjectType WIG
        | "--bundle" -> setProjectType Bundle
        | "--html" -> setProjectType Html
        | "--site" -> setProjectType WebSite
        | "--printjs" -> wsArgs := { !wsArgs with PrintJS = true }
        | "--vserrors" ->
            wsArgs := { !wsArgs with VSStyleErrors = true }
            fscArgs.Add a
        | StartsWith "--wsoutput:" o ->
            wsArgs := { !wsArgs with OutputPath = Some o }
        | StartsWith "--fsc:" p ->
            wsArgs := { !wsArgs with FscPath = p }
        | StartsWith "--project:" p ->
            wsArgs := { !wsArgs with ProjectFile = p }
        | StartsWith "--doc:" d ->
            wsArgs := { !wsArgs with Documentation = Some d }
            fscArgs.Add a
        | StartsWith "-o:" o ->
            wsArgs := { !wsArgs with AssemblyFile = o }
            fscArgs.Add a
        | StartsWith "-r:" r | StartsWith "--reference:" r ->
            refs.Add r
            fscArgs.Add a
        | _ -> 
            fscArgs.Add a  
    wsArgs := { !wsArgs with References = refs.ToArray(); FscArgs = fscArgs.ToArray() }

#if DEBUG
    Compile !wsArgs
    0 
#else
    try
        Compile !wsArgs
        logf "Stopped at: %A" System.DateTime.Now
        0       
    with e ->
        (!wsArgs).Documentation |> Option.iter (fun d -> if File.Exists d then File.Delete d)
        logf "Failed at: %A" System.DateTime.Now
        logf "Error: %A" e
        1
#endif