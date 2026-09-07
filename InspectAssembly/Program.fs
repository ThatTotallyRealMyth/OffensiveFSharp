namespace InspectAssembly

open System
open System.Collections.Generic
open System.Diagnostics
open System.IO
open System.Reflection
open Mono.Cecil

module Program =
    [<Literal>]
    let BF_DESERIALIZE = "System.Runtime.Serialization.Formatters.Binary.BinaryFormatter::Deserialize"

    [<Literal>]
    let DC_JSON_READ_OBJ = "System.Runtime.Serialization.Json.DataContractJsonSerializer::ReadObject"

    [<Literal>]
    let DC_XML_READ_OBJ = "System.Runtime.Serialization.Xml.DataContractSerializer::ReadObject"

    [<Literal>]
    let JS_SERIALIZER_DESERIALIZE = "System.Web.Script.Serialization.JavaScriptSerializer::Deserialize"

    [<Literal>]
    let LOS_FORMATTER_DESERIALIZE = "System.Web.UI.LosFormatter::Deserialize"

    [<Literal>]
    let NET_DATA_CONTRACT_READ_OBJ = "System.Runtime.Serialization.NetDataContractSerializer::ReadObject"

    [<Literal>]
    let NET_DATA_CONTRACT_DESERIALIZE = "System.Runtime.Serialization.NetDataContractSerializer::Deserialize"

    [<Literal>]
    let OBJ_STATE_FORMATTER_DESERIALIZE = "System.Web.UI.ObjectStateFormatter::Deserialize"

    [<Literal>]
    let SOAP_FORMATTER_DESERIALIZE = "System.Runtime.Serialization.Formatters.Soap.SoapFormatter::Deserialize"

    [<Literal>]
    let XML_SERIALIZER_DESERIALIZE = "System.Xml.Serialization.XmlSerializer::Deserialize"

    [<Literal>]
    let REGISTER_CHANNEL = "System.Runtime.Remoting.Channels.ChannelServices::RegisterChannel"

    [<Literal>]
    let WCF_SERVER_STRING = "System.ServiceModel.ServiceHost::AddServiceEndpoint"

    [<Literal>]
    let WCF_SERVER_ALT_STRING = "System.ServiceModel.Channels.CommunicationObject::Open"

    [<Literal>]
    let WCF_CLIENT_STRING = "System.ServiceModel.ChannelFactory::CreateChannel"

    type PidArgument =
        | One of int
        | Many of int array
        | All

    type Arguments =
        {
            Recurse: bool
            ThirdParty: bool
            Verbosity: int
            Path: string option
            Pid: PidArgument option
            OutFile: string option
        }

    [<StructuralEquality; StructuralComparison>]
    type AnalysisMethod =
        {
            MethodName: string
            FilterLevel: string option
        }
        override this.ToString() =
            match this.FilterLevel with
            | Some level -> sprintf "%s (Filter Level: %s)" this.MethodName level
            | None -> this.MethodName

    type GadgetItem =
        {
            IsDotNetRemoting: bool
            RemotingChannel: string
            IsWCFServer: bool
            IsWCFClient: bool
            GadgetName: string
            FilterLevel: string option
            MethodAppearance: string
        }

    type AssemblyGadgetAnalysis =
        {
            AssemblyName: string
            RemotingChannels: string array
            SerializationGadgetCalls: Map<string, AnalysisMethod list>
            WcfServerCalls: Map<string, AnalysisMethod list>
            ClientCalls: Map<string, AnalysisMethod list>
            RemotingCalls: Map<string, AnalysisMethod list>
        }

    let usage () =
        """
Usage:
    InspectAssembly.exe [-r|--recurse] [-v|--verbose] [-vv|--very-verbose]
                        [path="<PATH>"] [pid=<PID | PID,... | all>] [outfile="<PATH>"]

Flags:
    -r, --recurse           Recursively search the specified directory.
                            Only used when 'path=<DIRECTORY>'.
    -t, --third-party       Only show results for non-Microsoft assemblies.
    -v, --verbose           Print the path of skipped assemblies; only used with '--third-party' flag
    -vv, --very-verbose     Print the path of each assembly checked.

Arguments:
    path    - A path to a .NET binary to analyze, or a directory containing .NET assemblies.
    pid     - An integer or comma-separated list of integers to analyze.
              If the keyword 'all' is passed, then all processes are analyzed.
    outfile - File to write results to.

Examples:
    InspectAssembly.exe path="C:\Windows\System32\powershell.exe"
    InspectAssembly.exe path="C:\Windows\System32\"
    InspectAssembly.exe -r path="C:\Program Files\" --third-party -v
    InspectAssembly.exe pid=12044
    InspectAssembly.exe pid=12044,12300 outfile=proc_analysis.txt
    InspectAssembly.exe pid=all
"""

    let removeWrappingQuotes (value: string) =
        let value = value.Replace("\"", "")

        if value.Length >= 2
           && ((value.StartsWith("'") && value.EndsWith("'"))
               || (value.StartsWith("\"") && value.EndsWith("\""))) then
            value.Substring(1, value.Length - 2)
        else
            value

    let argParser (args: string array) =
        let values = Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        let mutable recurse = false
        let mutable thirdParty = false
        let mutable verbosity = 0

        for arg in args do
            match arg.ToLowerInvariant() with
            | "-r" | "--recurse" -> recurse <- true
            | "-v" | "--verbose" -> verbosity <- max verbosity 1
            | "-vv" | "--very-verbose" -> verbosity <- 2
            | "-t" | "--third-party" -> thirdParty <- true
            | _ when arg.Contains('=') ->
                let parts = arg.Split([| '=' |], 2)

                if parts.Length = 2 && not (String.IsNullOrEmpty(parts.[1])) then
                    values.[parts.[0]] <- parts.[1]
                else
                    Console.Error.WriteLine("Argument '{0}' contained an empty value. Skipping.", arg)
            | _ -> Console.Error.WriteLine("Argument '{0}' is not of format 'key=val'. Skipping.", arg)

        if not (values.ContainsKey("path")) && not (values.ContainsKey("pid")) then
            invalidArg "args" "Not enough arguments given. Must provide 'path', 'pid', or both."

        let path =
            if values.ContainsKey("path") then
                let path = removeWrappingQuotes values.["path"]

                if not (File.Exists(path)) && not (Directory.Exists(path)) then
                    invalidArg "path" (sprintf "File or directory %s does not exist." path)

                Some path
            else
                None

        let pid =
            if not (values.ContainsKey("pid")) then
                None
            else
                let value = values.["pid"]

                if value.Equals("all", StringComparison.OrdinalIgnoreCase) then
                    Some All
                elif value.Contains(',') then
                    let pids =
                        value.Split(',')
                        |> Array.choose (fun pid ->
                            match Int32.TryParse(pid) with
                            | true, parsed -> Some parsed
                            | _ -> None)

                    Some(Many pids)
                else
                    match Int32.TryParse(value) with
                    | true, parsed -> Some(One parsed)
                    | _ ->
                        invalidArg
                            "pid"
                            (sprintf "Given invalid pid: %s. Argument 'pid' must be one of integer, integer list, or value 'all'." value)

        {
            Recurse = recurse
            ThirdParty = thirdParty
            Verbosity = verbosity
            Path = path
            Pid = pid
            OutFile = if values.ContainsKey("outfile") then Some(removeWrappingQuotes values.["outfile"]) else None
        }

    // Directory.GetFiles() returns prematurely when a child directory cannot be read,
    // so recurse manually and ignore inaccessible sub-directories.
    let rec getFiles path recurse =
        seq {
            yield! Directory.EnumerateFiles(path, "*.exe")
            yield! Directory.EnumerateFiles(path, "*.dll")

            if recurse then
                for subDirectory in Directory.EnumerateDirectories(path) do
                    try
                        yield! getFiles subDirectory true
                    with
                    | :? UnauthorizedAccessException ->
                        Console.Error.WriteLine("[*] Unable to check '{0}'; access denied", subDirectory)
                    | ex -> Console.Error.WriteLine("[-] Error: {0}", ex.Message)
        }

    let rec getTypes (types: TypeDefinition seq) =
        seq {
            for typeDefinition in types do
                yield typeDefinition
                yield! getTypes typeDefinition.NestedTypes
        }

    let gadgetForOperand (operand: string) =
        [
            BF_DESERIALIZE
            DC_JSON_READ_OBJ
            DC_XML_READ_OBJ
            JS_SERIALIZER_DESERIALIZE
            LOS_FORMATTER_DESERIALIZE
            NET_DATA_CONTRACT_READ_OBJ
            NET_DATA_CONTRACT_DESERIALIZE
            OBJ_STATE_FORMATTER_DESERIALIZE
            SOAP_FORMATTER_DESERIALIZE
            XML_SERIALIZER_DESERIALIZE
        ]
        |> List.tryFind operand.Contains

    let analyzeAssembly (assemblyName: string) =
        let gadgets = ResizeArray<GadgetItem>()
        let mutable remotingChannelParts = [||]
        let mutable typeFilterLevel = "ldc.i4.2"
        let mutable filterLevel = "Low"

        use assembly = AssemblyDefinition.ReadAssembly(assemblyName)

        let methods =
            getTypes assembly.MainModule.Types
            |> Seq.collect (fun typeDefinition ->
                typeDefinition.Methods
                |> Seq.filter (fun methodDefinition -> methodDefinition.HasBody)
                |> Seq.map (fun methodDefinition -> typeDefinition, methodDefinition))

        for typeDefinition, methodDefinition in methods do
            for instruction in methodDefinition.Body.Instructions do
                let opcode = instruction.OpCode.ToString()
                let operand = if isNull instruction.Operand then "" else instruction.Operand.ToString()
                let mutable gadgetName = ""
                let mutable isRemoting = false
                let mutable remotingChannel = ""
                let mutable isWCFServer = false
                let mutable isWCFClient = false

                if opcode = "callvirt" then
                    match gadgetForOperand operand with
                    | Some gadget -> gadgetName <- gadget
                    | None when operand.Contains(WCF_SERVER_STRING) ->
                        gadgetName <- WCF_SERVER_STRING
                        isWCFServer <- true
                    | None when operand.Contains(WCF_SERVER_ALT_STRING) ->
                        gadgetName <- WCF_SERVER_ALT_STRING
                        isWCFServer <- true
                    | None when operand.Contains("System.ServiceModel.ChannelFactory") && operand.Contains("CreateChannel") ->
                        gadgetName <- WCF_CLIENT_STRING
                        isWCFClient <- true
                    | None when operand.Contains("set_FilterLevel(System.Runtime.Serialization.Formatters.TypeFilterLevel)") ->
                        if typeFilterLevel.EndsWith("3", StringComparison.Ordinal) then
                            filterLevel <- "Full"
                    | None -> ()
                elif opcode.StartsWith("ldc.i4", StringComparison.Ordinal) then
                    typeFilterLevel <- opcode
                elif opcode = "newobj" && operand.Contains("System.Runtime.Remoting.Channels.") then
                    remotingChannelParts <- operand.Split('.')
                elif opcode = "call" && operand.Contains(REGISTER_CHANNEL) then
                    isRemoting <- true
                    gadgetName <- REGISTER_CHANNEL

                    remotingChannel <-
                        if remotingChannelParts.Length > 5 then remotingChannelParts.[5] else "Unknown"

                if not (String.IsNullOrEmpty(gadgetName)) || isWCFClient || isWCFServer || isRemoting then
                    gadgets.Add(
                        {
                            GadgetName = gadgetName
                            IsDotNetRemoting = isRemoting
                            RemotingChannel = remotingChannel
                            IsWCFClient = isWCFClient
                            IsWCFServer = isWCFServer
                            MethodAppearance = sprintf "%s.%s" typeDefinition.Name methodDefinition.Name
                            FilterLevel = if gadgetName.Contains(BF_DESERIALIZE) then Some filterLevel else None
                        })

        let group predicate =
            gadgets
            |> Seq.filter predicate
            |> Seq.groupBy (fun gadget -> gadget.GadgetName)
            |> Seq.filter (fun (name, _) -> not (String.IsNullOrEmpty(name)))
            |> Seq.map (fun (name, items) ->
                name,
                (items
                 |> Seq.map (fun item ->
                     {
                         MethodName = item.MethodAppearance
                         FilterLevel = item.FilterLevel
                     })
                 |> Seq.distinct
                 |> Seq.toList))
            |> Map.ofSeq

        {
            AssemblyName = assemblyName
            RemotingChannels =
                gadgets
                |> Seq.filter (fun gadget -> gadget.IsDotNetRemoting)
                |> Seq.map (fun gadget -> gadget.RemotingChannel)
                |> Seq.filter (String.IsNullOrEmpty >> not)
                |> Seq.distinct
                |> Seq.toArray
            SerializationGadgetCalls =
                group (fun gadget -> not gadget.IsWCFClient && not gadget.IsWCFServer && not gadget.IsDotNetRemoting)
            ClientCalls = group (fun gadget -> gadget.IsWCFClient)
            WcfServerCalls = group (fun gadget -> gadget.IsWCFServer)
            RemotingCalls = group (fun gadget -> gadget.IsDotNetRemoting)
        }

    let inspectAssembly path thirdPartyOnly verbosity =
        if thirdPartyOnly && FileVersionInfo.GetVersionInfo(path).CompanyName = "Microsoft Corporation" then
            if verbosity > 0 then
                Console.Error.WriteLine("[*] Skipping Microsoft assembly: {0}", path)

            invalidOp "Microsoft Assembly"
        elif verbosity > 1 then
            Console.Error.WriteLine("[*] Checking: {0}", path)

        // AssemblyName.GetAssemblyName throws for native binaries.
        AssemblyName.GetAssemblyName(path) |> ignore
        analyzeAssembly path

    let formatGadgetName (name: string) =
        let parts = name.Replace("::", "|").Split('|')

        if parts.Length <> 2 then
            name
        else
            let typeParts = parts.[0].Split('.')
            sprintf "%s::%s()" typeParts.[typeParts.Length - 1] parts.[1]

    let formatGadgets (calls: Map<string, AnalysisMethod list>) =
        let writer = Text.StringBuilder()

        for KeyValue(name, methods) in calls do
            writer.AppendFormat("    {0} is called in the following methods:\n", formatGadgetName name) |> ignore

            for methodInfo in methods do
                writer.AppendFormat("      {0}\n", methodInfo) |> ignore

            writer.AppendLine() |> ignore

        writer.ToString()

    let formatAnalysis (analysis: AssemblyGadgetAnalysis) =
        let writer = Text.StringBuilder()

        if not analysis.RemotingCalls.IsEmpty then
            writer.AppendLine("  .NET Remoting:") |> ignore
            writer.Append(formatGadgets analysis.RemotingCalls) |> ignore
            writer.AppendLine("    Remoting Channels:") |> ignore

            for channel in analysis.RemotingChannels do
                writer.AppendFormat("      {0}\n", channel) |> ignore

        if not analysis.ClientCalls.IsEmpty then
            writer.AppendLine("  WCFClient Gadgets:") |> ignore
            writer.Append(formatGadgets analysis.ClientCalls) |> ignore

        if not analysis.WcfServerCalls.IsEmpty then
            writer.AppendLine("  WCFServer Gadgets:") |> ignore
            writer.Append(formatGadgets analysis.WcfServerCalls) |> ignore

        if not analysis.SerializationGadgetCalls.IsEmpty then
            writer.AppendLine("  Serialization Gadgets:") |> ignore
            writer.Append(formatGadgets analysis.SerializationGadgetCalls) |> ignore

        if writer.Length = 0 then
            ""
        else
            let fileInfo = FileVersionInfo.GetVersionInfo(analysis.AssemblyName)

            sprintf
                "Assembly Name: %s\n\n  ProductName     : %s\n  ProductVersion  : %s\n  FileVersion     : %s\n  CompanyName     : %s\n  LegalCopyright  : %s\n\n%s"
                analysis.AssemblyName
                fileInfo.ProductName
                fileInfo.ProductVersion
                fileInfo.FileVersion
                fileInfo.CompanyName
                fileInfo.LegalCopyright
                (writer.ToString())

    let tryInspect (results: ResizeArray<AssemblyGadgetAnalysis>) (arguments: Arguments) (path: string) =
        try
            results.Add(inspectAssembly path arguments.ThirdParty arguments.Verbosity)
        with _ ->
            ()

    let getProcesses (pidArgument: PidArgument) =
        match pidArgument with
        | One pid ->
            try
                [ Process.GetProcessById(pid) ]
            with ex ->
                Console.Error.WriteLine("[-] Error: {0}", ex.Message)
                []
        | Many pids ->
            pids
            |> Array.choose (fun pid ->
                try
                    Some(Process.GetProcessById(pid))
                with _ ->
                    None)
            |> Array.toList
        | All -> Process.GetProcesses() |> Array.toList

    [<EntryPoint>]
    let main args =
        let arguments =
            try
                Some(argParser args)
            with ex ->
                Console.Error.WriteLine("[-] Error parsing arguments. {0}", ex.Message)
                Console.Error.WriteLine(usage ())
                None

        match arguments with
        | None -> 1
        | Some arguments ->
            let results = ResizeArray<AssemblyGadgetAnalysis>()

            match arguments.Path with
            | Some path when File.Exists(path) -> tryInspect results arguments path
            | Some path ->
                for file in getFiles path arguments.Recurse do
                    tryInspect results arguments file
            | None -> ()

            match arguments.Pid with
            | Some pidArgument ->
                let processes = getProcesses pidArgument

                if List.isEmpty processes then
                    Console.Error.WriteLine("[-] Failed to acquire any processes given pid argument.")

                for proc in processes do
                    use proc = proc

                    try
                        tryInspect results arguments proc.MainModule.FileName
                    with _ ->
                        ()
            | None -> ()

            let result =
                results
                |> Seq.map formatAnalysis
                |> Seq.filter (String.IsNullOrEmpty >> not)
                |> String.concat "\n"

            if String.IsNullOrEmpty(result) then
                Console.Error.WriteLine("[-] No results to display.")
            else
                Console.WriteLine(result)

                match arguments.OutFile with
                | Some outputPath when File.Exists(outputPath) ->
                    Console.Error.WriteLine("[-] File {0} already exists - will not write data to outfile.", outputPath)
                | Some outputPath ->
                    try
                        File.WriteAllText(outputPath, result)
                        Console.Error.WriteLine("[+] Wrote results to {0}", outputPath)
                    with _ ->
                        Console.Error.WriteLine("[-] Failed to write output file: {0}", outputPath)
                | None -> ()

            0
