﻿module Stores

open System
open System.Diagnostics.Tracing

open FsProfiler

type private TraceEvent =
| TaskStart of Guid * Guid option * string
| TaskStop of Guid * Guid option * int64

type Task =
    {
        Id : Guid
        Name : string
        SubTasks : Task list
        DurationInMilliseconds : int64
    }

type MemoryStore () as this =
    inherit EventListener ()

    do
        this.EnableEvents(FsProfilerEvents.Log, EventLevel.LogAlways)        

    let queue = new System.Collections.Concurrent.ConcurrentQueue<TraceEvent> ()

    member __.GetTasks () = 
        let tasks = queue |> Seq.toList
        let rec mapTask parent c =
            match c with
            | TaskStart (id, p, name) when p = parent -> 
                let timing = tasks |> List.pick (fun c -> 
                    match c with 
                    | TaskStop (cid, p, time) when id = cid && p = parent -> time |> Some
                    | _ -> None)
                { 
                    Id = id
                    Name = name
                    SubTasks = tasks |> List.choose (mapTask (id |> Some))
                    DurationInMilliseconds = timing 
                } |> Some
            | _ -> None

        tasks
        |> List.map (mapTask None)
        |> List.choose id

    override __.OnEventWritten args =
        if args.EventSource.Name = "FsProfilerEvents" then
            match args.EventId with
            | 1 -> TaskStart(args.Payload.[1] :?> Guid, None, args.Payload.[0] :?> string)
            | 2 -> TaskStart(args.Payload.[2] :?> Guid, args.Payload.[3] :?> Guid |> Some, args.Payload.[0] :?> string)
            | 3 -> TaskStop(args.Payload.[2] :?> Guid, None, args.Payload.[1] :?> int64)
            | 4 -> TaskStop(args.Payload.[3] :?> Guid, args.Payload.[4] :?> Guid |> Some, args.Payload.[2] :?> int64)
            | _ -> failwith "Unkown event"

            |> queue.Enqueue
