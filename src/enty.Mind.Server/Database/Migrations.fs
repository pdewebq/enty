module enty.Mind.Server.Database.Migrations

open System
open FluentMigrator
open FluentMigrator.Runner
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection


[<Migration(20210508172000L)>]
type Init() =
    inherit Migration()

    override this.Up() =
        this.Create.Table("entities")
            .WithColumn("id").AsGuid().PrimaryKey().NotNullable()
            .WithColumn("sense").AsCustom("jsonb").NotNullable()
            .WithColumn("created_dts").AsDateTime().NotNullable()
            .WithColumn("updated_dts").AsDateTime().NotNullable()
        |> ignore

    override this.Down() =
        this.Delete.Table("entities")
        |> ignore


let configureMigrations (services: IServiceCollection) (connectionString: string) =
    services
//        .AddLogging(fun b -> b.AddFluentMigratorConsole() |> ignore)
        .AddFluentMigratorCore()
        .ConfigureRunner(fun b ->
            b.AddPostgres()
                .WithGlobalConnectionString(connectionString)
                .ScanIn(typeof<Init>.Assembly).For.Migrations()
            |> ignore
        )

let migrate (app: IApplicationBuilder) =
    use scope = app.ApplicationServices.CreateScope()
    let runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>()
    runner.MigrateUp()
