namespace AbandonedCOMKeys

open System
open System.IO
open System.Management

module Program =
    [<EntryPoint>]
    let main _ =
        try
            use searcher =
                new ManagementObjectSearcher(
                    "root\\CIMV2",
                    "SELECT * FROM Win32_ClassicCOMClassSetting")

            let inprocsvr32 = ResizeArray<string>()

            // Query all objects for their InProcSvr32 value and if not null, check that the file still exists
            for queryObj in searcher.Get() do
                let inprocsvrStr = Convert.ToString(queryObj.["InprocServer32"])
                let path = Environment.ExpandEnvironmentVariables(inprocsvrStr).Trim('"')

                if not (String.IsNullOrWhiteSpace(path)) && not (File.Exists(path)) then
                    let clsidStr = Convert.ToString(queryObj.["ComponentID"])
                    let missingKey = path + "," + clsidStr

                    if missingKey.StartsWith("C:", StringComparison.OrdinalIgnoreCase) then
                        inprocsvr32.Add(missingKey)

            inprocsvr32
            |> Seq.filter (String.IsNullOrWhiteSpace >> not)
            |> Seq.distinct
            |> Seq.iter Console.WriteLine

            0
        with :? ManagementException as ex ->
            Console.WriteLine("An error occurred while querying for WMI data: " + ex.Message)
            1

