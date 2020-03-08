namespace TestProjectCore31

open WebSharper

module Client =
    [<JavaScript>]
    type SomeRecord = { Name : string }
    [<Rpc>] 
    let DoSomething () = async { return { Name = "Hallo" } }

    [<SPAEntryPoint>]
    let EntryPoint = ()

    [<EntryPoint>]
    let main args = 0