namespace PhantomService

open System
open System.ComponentModel
open System.Runtime.InteropServices
open System.ServiceProcess
open System.Text

module Win32 =
    [<Literal>]
    let SC_MANAGER_CONNECT = 0x0001u

    [<Literal>]
    let DELETE = 0x00010000u

    [<DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)>]
    extern nativeint OpenSCManager(string machineName, string databaseName, uint32 desiredAccess)

    [<DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)>]
    extern nativeint OpenService(nativeint serviceManager, string serviceName, uint32 desiredAccess)

    [<DllImport("advapi32.dll", SetLastError = true)>]
    extern bool DeleteService(nativeint service)

    [<DllImport("advapi32.dll", SetLastError = true)>]
    extern bool CloseServiceHandle(nativeint handle)

module Program =
    let removeService serviceName =
        let serviceManager = Win32.OpenSCManager(null, null, Win32.SC_MANAGER_CONNECT)

        if serviceManager = 0n then
            raise (Win32Exception(Marshal.GetLastWin32Error()))

        try
            let service = Win32.OpenService(serviceManager, serviceName, Win32.DELETE)

            if service = 0n then
                raise (Win32Exception(Marshal.GetLastWin32Error()))

            try
                if not (Win32.DeleteService(service)) then
                    raise (Win32Exception(Marshal.GetLastWin32Error()))
            finally
                Win32.CloseServiceHandle(service) |> ignore
        finally
            Win32.CloseServiceHandle(serviceManager) |> ignore

    let removePhantomServices remove =
        Console.OutputEncoding <- Encoding.Unicode

        for service in ServiceController.GetServices() do
            use service = service
            let serviceName = service.ServiceName

            if Encoding.UTF8.GetByteCount(serviceName) <> serviceName.Length then
                Console.WriteLine("[*] Found non-ASCII service: " + serviceName)

                if remove then
                    try
                        removeService serviceName
                        Console.WriteLine("[+] Removed {0}", serviceName)
                    with :? Win32Exception as ex ->
                        Console.WriteLine("[-] Failed to remove {0} -> {1}", serviceName, ex.Message)

    [<EntryPoint>]
    let main args =
        let usage = "PhantomService.exe (audit|remove)"

        match args |> Array.map (fun arg -> arg.ToLowerInvariant()) with
        | [| "audit" |] ->
            removePhantomServices false
            0
        | [| "remove" |] ->
            removePhantomServices true
            0
        | _ ->
            Console.WriteLine(usage)
            1

