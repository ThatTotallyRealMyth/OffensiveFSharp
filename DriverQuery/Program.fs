namespace DriverQuery

open System
open System.Collections.Generic
open System.Diagnostics
open System.IO
open System.Management
open System.Security.Cryptography
open System.Security.Cryptography.X509Certificates
open System.ServiceProcess

module Program =
    // Hack to resolve file location issues caused by NT paths
    let fixPath (oldPath: string) =
        if oldPath.StartsWith("\\SystemRoot\\", StringComparison.OrdinalIgnoreCase) then
            oldPath.Replace("\\SystemRoot\\", Environment.GetFolderPath(Environment.SpecialFolder.Windows) + "\\")
        elif oldPath.StartsWith("system32\\", StringComparison.OrdinalIgnoreCase) then
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), oldPath)
        elif oldPath.StartsWith("\\??\\", StringComparison.Ordinal) then
            oldPath.Substring(4)
        else
            oldPath

    let enumAllKernelDriverServices () =
        let drivers = Dictionary<string, string>()

        for service in ServiceController.GetDevices() do
            use service = service

            if (service.ServiceType &&& ServiceType.KernelDriver) <> enum<ServiceType> 0 then
                use wmiService = new ManagementObject(sprintf "Win32_Service.Name='%s'" service.ServiceName)

                try
                    wmiService.Get()
                    let pathName = Convert.ToString(wmiService.["PathName"])

                    if not (String.IsNullOrEmpty(pathName)) then
                        drivers.[service.ServiceName] <-
                            pathName
                            |> Environment.ExpandEnvironmentVariables
                            |> fixPath
                with _ ->
                    ()

        drivers

    let printDriver serviceName driverFile (fileInfo: FileVersionInfo) (file: FileInfo) (certificate: X509Certificate2) =
        Console.WriteLine(
            "{0}\n    Service Name: {1}\n    Path: {2}\n    Version: {3}\n    Creation Time (UTC): {4}\n    Cert Issuer: {5}\n    Signer: {6}\n",
            fileInfo.FileDescription,
            serviceName,
            driverFile,
            fileInfo.FileVersion,
            file.CreationTimeUtc,
            certificate.Issuer,
            certificate.Subject)

    let getSignatures nonMicrosoftOnly debugOutput =
        Console.WriteLine("[+] Enumerating driver services...")
        let drivers = enumAllKernelDriverServices ()
        Console.WriteLine("[+] Checking file signatures...")

        for KeyValue(serviceName, driverFile) in drivers do
            try
                let file = FileInfo(driverFile)
                let fileInfo = FileVersionInfo.GetVersionInfo(driverFile)
                use certificate = X509Certificate.CreateFromSignedFile(driverFile)
                use certificate2 = new X509Certificate2(certificate)

                if not nonMicrosoftOnly
                   || not (certificate2.Subject.Contains("Microsoft Corporation", StringComparison.OrdinalIgnoreCase)) then
                    printDriver serviceName driverFile fileInfo file certificate2
            with
            | :? CryptographicException ->
                if debugOutput then
                    Console.WriteLine("[-] Invalid certificate handle on {0}. Skipping...", driverFile)
            | :? FileNotFoundException ->
                if debugOutput then
                    Console.WriteLine("[-] Couldn't find the file {0}. Skipping...", driverFile)

    [<EntryPoint>]
    let main args =
        let usage = "DriverQuery.exe <no-msft> <debug>"

        if args.Length > 2 then
            Console.WriteLine(usage)
            1
        else
            let nonMicrosoftOnly = args.Length > 0 && args.[0].Contains("no-msft", StringComparison.OrdinalIgnoreCase)
            let debugOutput = args.Length > 1 && args.[1].Contains("debug", StringComparison.OrdinalIgnoreCase)
            getSignatures nonMicrosoftOnly debugOutput
            0

