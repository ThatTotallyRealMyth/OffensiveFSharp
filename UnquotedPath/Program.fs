namespace UnquotedPath

open System
open Microsoft.Win32

module Program =
    let isVulnerablePath (path: string) =
        not (String.IsNullOrEmpty(path))
        && not (path.Contains('"'))
        && path.Contains(' ')
        && not (path.Contains("System32", StringComparison.OrdinalIgnoreCase))
        && not (path.Contains("SysWOW64", StringComparison.OrdinalIgnoreCase))

    [<EntryPoint>]
    let main _ =
        use services = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services")

        let paths =
            services.GetSubKeyNames()
            |> Seq.choose (fun serviceName ->
                use service = services.OpenSubKey(serviceName)

                if isNull service then
                    None
                else
                    let path = Convert.ToString(service.GetValue("ImagePath"))
                    if isVulnerablePath path then Some path else None)
            |> Seq.distinct
            |> Seq.toList

        match paths with
        | [] -> Console.WriteLine("[-] Couldn't find any unquoted services paths")
        | _ ->
            Console.WriteLine("[+] Unquoted service paths found")
            paths |> Seq.iter Console.WriteLine

        0

