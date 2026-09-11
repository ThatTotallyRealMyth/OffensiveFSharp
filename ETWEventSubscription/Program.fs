namespace ETWEventSubscription

open System
open System.Security.Principal
open Microsoft.Diagnostics.Tracing
open Microsoft.Diagnostics.Tracing.Parsers
open Microsoft.Diagnostics.Tracing.Parsers.Kernel
open Microsoft.Diagnostics.Tracing.Session

module Program =
    let usage =
        "Usage:\n" +
        "    ETWEventSubscription.exe -UserLogon\n" +
        "    ETWEventSubscription.exe -ProcStart <keyword>"

    let reportEvent (description: string) =
        Console.WriteLine("[+] {0}", description)

    let isAdmin () =
        use identity = WindowsIdentity.GetCurrent()
        let principal = WindowsPrincipal(identity)

        if principal.IsInRole(WindowsBuiltInRole.Administrator) then
            true
        else
            Console.WriteLine("[-] You do not have admin privileges required to use the provider. Exiting.")
            false

    let userLogon () =
        Console.WriteLine("Waiting for a user to log on...")
        let sessionName = "UserSession"

        use session = new TraceEventSession(sessionName, null)
        session.StopOnDispose <- true

        use source = new ETWTraceEventSource(sessionName, TraceEventSourceType.Session)
        let parser = RegisteredTraceEventParser(source)

        parser.add_All(
            Action<TraceEvent>(fun data ->
                let message = data.FormattedMessage

                if not (String.IsNullOrEmpty(message))
                   && message.Contains("Authentication stopped. Result 0", StringComparison.Ordinal) then
                    reportEvent "User login detected"))

        Console.CancelKeyPress.Add(fun args ->
            args.Cancel <- true
            session.Stop() |> ignore)

        let provider = Guid("DBE9B383-7CF3-4331-91CC-A3CB16A3B538")
        session.EnableProvider(provider, TraceEventLevel.Verbose) |> ignore
        source.Process() |> ignore

    let procStart (procKeyword: string) =
        Console.WriteLine("Waiting for a process containing \"{0}\" to start...", procKeyword)

        use session = new TraceEventSession("KernelSession")
        session.StopOnDispose <- true

        session.Source.Kernel.add_ProcessStart(
            Action<ProcessTraceData>(fun data ->
                if data.ProcessName.Contains(procKeyword, StringComparison.OrdinalIgnoreCase) then
                    reportEvent (
                        sprintf
                            "Detected execution of %s matching keyword \"%s\""
                            data.ProcessName
                            procKeyword)))

        Console.CancelKeyPress.Add(fun args ->
            args.Cancel <- true
            session.Stop() |> ignore)

        session.EnableKernelProvider(KernelTraceEventParser.Keywords.Process) |> ignore
        session.Source.Process() |> ignore

    [<EntryPoint>]
    let main args =
        match args with
        | [| command |] when command.Equals("-UserLogon", StringComparison.OrdinalIgnoreCase) ->
            if isAdmin () then
                userLogon ()
                0
            else
                1
        | [| command; procKeyword |] when command.Equals("-ProcStart", StringComparison.OrdinalIgnoreCase) ->
            if isAdmin () then
                procStart procKeyword
                0
            else
                1
        | [| command |] when command.Equals("-ProcStart", StringComparison.OrdinalIgnoreCase) ->
            Console.WriteLine("[-] Missing target process keyword (powersh, MsMp, etc.)")
            1
        | _ ->
            Console.WriteLine(usage)
            1
