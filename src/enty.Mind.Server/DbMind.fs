namespace enty.Mind.Server

open System
open System.Linq
open FSharp.Control
open LinqToDB
open LinqToDB.Configuration
open LinqToDB.Data
open LinqToDB.Mapping
open Newtonsoft.Json.Linq
open enty.Core
open enty.Mind
open enty.Mind.Server.SenseJToken
open enty.Mind.Server.LinqToDbPostgresExtensions



[<Table(Name="Entities")>]
type EntityDao =
    { [<Column "Id">] Id: Guid
      [<Column "Sense">] Sense: obj }

type EntyDataConnection(options: LinqToDbConnectionOptions<EntyDataConnection>) =
    inherit DataConnection(options)
    member this.Entities = this.GetTable<EntityDao>()


type DbMind(db: EntyDataConnection) =

    interface IMind with
        member this.Remember(EntityId entityId, sense) = async {
            let senseJson = (Sense.toJToken sense).ToString()
            do! db.Entities
                    .Value((fun x -> x.Id), entityId)
                    .Value((fun x -> x.Sense), ((fun () -> Sql.Json.AsJsonb(senseJson))))
                    .InsertAsync()
                |> Async.AwaitTask
                |> Async.Ignore
            ()
        }

        member this.Forget(EntityId entityId) = async {
            let q = query {
                for entity in db.Entities do
                where (entity.Id = entityId)
            }

            do! q.DeleteAsync() |> Async.AwaitTask |> Async.Ignore
        }

        member this.GetEntities(eids) = async {
            let eids = eids |> Seq.map (fun (EntityId x) -> x)
            let q = query {
                for entity in db.Entities do
                where (eids.Contains(entity.Id))
                select (entity.Id, Sql.AsText(entity.Sense))
            }
            let! entityDaos = q.ToListAsync() |> Async.AwaitTask
            let entities =
                entityDaos
                |> Seq.map (fun (eid, senseString) ->
                    { Entity.Id = EntityId eid
                      Sense = JToken.Parse(senseString) |> Sense.ofJToken }
                )
                |> Seq.toArray

            return entities
        }

        member this.Wish(wish, offset, limit) = async {
            let selectEntitiesByIds ids = query {
                for e in db.Entities do
                join id in ids on (e.Id = id)
                select e
            }
            let stringPath path =
                path
                |> Seq.map (function
                    | WishPathEntry.ListEntry -> "[*]"
                    | WishPathEntry.MapEntry key -> $".{key}"
                )
                |> String.concat ""
            let selectEntitiesByJsonpath jsonpath = query {
                for entity in db.Entities do
                where (Sql.Json.op_AtAt(entity.Sense, jsonpath))
                select entity
            }

            let rec queryWish wish =
                match wish with
                | Wish.ValueIs (path, value) ->
                    let path = stringPath path
                    let jsonpath = $"${path} == \"{value}\""
                    selectEntitiesByJsonpath jsonpath
                | Wish.MapFieldIs (path, key, value) ->
                    let path = stringPath path
                    let jsonpath = $"${path}.{key} == \"{value}\""
                    selectEntitiesByJsonpath jsonpath
                | Wish.ListContains (path, value) ->
                    let path = stringPath path
                    let jsonpath = $"${path}[*] == \"{value}\""
                    selectEntitiesByJsonpath jsonpath
                | Wish.Operator wishOperator -> queryWishOperator wishOperator

            and queryWishOperator wishOperator =
                match wishOperator with
                | WishOperator.Not wish ->
                    let nes = queryWish wish
                    let ids =
                        db.Entities.Select(fun e -> e.Id)
                            .Except(nes.Select(fun ne -> ne.Id))
                    selectEntitiesByIds ids
                | WishOperator.And (wish1, wish2) ->
                    let e1s = queryWish wish1
                    let e2s = queryWish wish2
                    let ids = query {
                        for e1 in e1s do
                        join e2 in e2s
                            on (e1.Id = e2.Id)
                        select e1.Id
                    }
                    selectEntitiesByIds (ids.Distinct())
                | WishOperator.Or (wish1, wish2) ->
                    let e1s = queryWish wish1
                    let e2s = queryWish wish2
                    let ids =
                        e1s.Select(fun e1 -> e1.Id)
                            .Union(e2s.Select(fun e2 -> e2.Id))
                            .Distinct()
                    selectEntitiesByIds ids


            let q = query {
                for e in queryWish wish do
                select e.Id
            }

            let! total = q.CountAsync() |> Async.AwaitTask
            let! eids = q.Skip(offset).Take(limit).Select(EntityId).ToArrayAsync() |> Async.AwaitTask

            return eids, total
        }
