namespace COMHunter

open System
open System.IO
open System.Management
open Microsoft.Win32

[<Struct>]
type COMServer =
    {
        CLSID: string
        ServerPath: string
        Type: string
    }

module Program =
    let usage = "Usage: COMHunter.exe <-inproc|-localserver>"

    let wmiCollection serverType =
        let comServers = ResizeArray<COMServer>()

        try
            use searcher =
                new ManagementObjectSearcher(
                    "root\\CIMV2",
                    "SELECT * FROM Win32_ClassicCOMClassSetting")

            for queryObj in searcher.Get() do
                let server =
                    Convert.ToString(queryObj.[serverType])
                    |> Environment.ExpandEnvironmentVariables
                    |> fun value -> value.Trim('"')

                if not (String.IsNullOrEmpty(server))
                   && not (server.Contains("c:\\windows\\", StringComparison.OrdinalIgnoreCase))
                   && File.Exists(server) then
                    comServers.Add(
                        {
                            CLSID = Convert.ToString(queryObj.["ComponentId"])
                            ServerPath = server
                            Type = serverType
                        })

            comServers
            |> Seq.sortBy (fun server -> server.ServerPath)
            |> Seq.toList
        with :? ManagementException as ex ->
            Console.WriteLine("[-] An error occurred while querying for WMI data: " + ex.Message)
            []

    let printServer server =
        Console.WriteLine("{0} {1} ({2})", server.CLSID, server.ServerPath, server.Type)

        // If the COM server is a .NET assembly, get the path of the actual DLL
        if server.ServerPath.Contains("mscoree.dll", StringComparison.OrdinalIgnoreCase) then
            let key =
                sprintf "HKEY_LOCAL_MACHINE\\SOFTWARE\\Classes\\CLSID\\%s\\InprocServer32\\1.0.0.0" server.CLSID

            let assembly = Registry.GetValue(key, "CodeBase", null)

            if not (isNull assembly) then
                Console.WriteLine(".NET Assembly: {0}", assembly)

                try
                    let defaultMethods = set [ "Equals"; "GetHashCode"; "GetType"; "ToString" ]
                    let assemblyType = Type.GetTypeFromCLSID(Guid.Parse(server.CLSID))

                    assemblyType.GetMethods()
                    |> Seq.filter (fun methodInfo -> not (defaultMethods.Contains(methodInfo.Name)))
                    |> Seq.iter (fun methodInfo -> Console.WriteLine("  Method: {0}", methodInfo.Name))
                with ex ->
                    Console.WriteLine("[-] Failed to inspect .NET COM server {0}: {1}", server.CLSID, ex.Message)

    [<EntryPoint>]
    let main args =
        match args |> Array.map (fun arg -> arg.ToLowerInvariant()) with
        | [||] ->
            wmiCollection "InprocServer32" @ wmiCollection "LocalServer32"
            |> Seq.iter printServer
            0
        | [| "-inproc" |] ->
            wmiCollection "InprocServer32" |> Seq.iter printServer
            0
        | [| "-localserver" |] ->
            wmiCollection "LocalServer32" |> Seq.iter printServer
            0
        | _ ->
            Console.WriteLine(usage)
            1
