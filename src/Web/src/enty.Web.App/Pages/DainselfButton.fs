module enty.Web.App.Pages.DainselfButton

open System
open Fable.Core
open Fable.Core.JsInterop
open Browser.Types
open Fable.SimpleHttp

open Feliz
open Feliz.MaterialUI
open Feliz.MaterialUI.Mui5

open FsToolkit.ErrorHandling
open enty.Core
open enty.Utils
open enty.Web.App
open enty.Web.App.Utils
open enty.Web.App.SenseFormatting
open enty.Web.App.SenseCreating.TagsShapeForm


type FilesState =
    | Empty
    | Selected of File array

[<ReactComponent>]
let PreviewImage (file: File) =
    let src = React.useMemo((fun () -> emitJsExpr (file) "URL.createObjectURL($0)"), [|file|])
    Mui.paper @+ [
        prop.sx {|
            height = 300
            width = 250
            display = "flex"
            justifyContent = "center"
            alignContent = "center"
        |}
    ] <| [
        Mui.box @+ [
            prop.sx {|
                backgroundImage = $"url({src})"
                backgroundSize = "contain"
                backgroundRepeat = "no-repeat"
                backgroundPosition = "center"
                height = "100%"
                width = "100%"
            |}
            prop.src src
        ] <| [ ]
    ]

[<ReactComponent>]
let DainselfButton () =
    let files, setFiles = React.useState(FilesState.Empty)
    let (tagsSense: Sense option), setTags = React.useState(None)

    let handleFilesSelected (files: File array) =
        setFiles (FilesState.Selected files)

    let tagsSenseChanged (sense: Validation<Sense, string>) =
        match sense with
        | Ok sense ->
            setTags (Some sense)
        | Error _ -> setTags None

    let handleBatchCreateClicked (files: File array) =
        async {
            for file in files do
                let form =
                    FormData.create ()
                    |> FormData.appendNamedFile "File" file.name file
                let! result = ResourceStorageHardcodeImpl.resourceStorage.Create(form)
                match result with
                | Ok uri ->
                    let sense =
                        Sense ^ senseMap {
                            "image", senseMap {
                                "resource", senseMap {
                                    "uri", string uri
                                    "content-length", string file.size
                                    "content-type", file.``type``
                                    "filename", file.name
                                }
                            }
                        }
                        |> Sense.merge ^
                            match tagsSense with
                            | Some tagsSense -> tagsSense
                            | None -> Sense.empty ()
                    let entityId = EntityId (Guid.NewGuid())
                    let! rememberResult = MindApiImpl.mindApi.Remember(entityId, sense |> Sense.format)
                    match rememberResult with
                    | Ok () -> ()
                    | Error error ->
                        Browser.Dom.window.alert($"Failed remember entity: {error}")
                | Error reason ->
                    Browser.Dom.window.alert($"Failed upload file: {reason}")
            Browser.Dom.window.alert("Finished")
        }
        |> Async.startSafe

    Mui.stack @+ [
        stack.spacing 1
    ] <| [
        Mui.button @+ [
            button.variant.outlined
            button.component' "label"
        ] <| [
            Html.text "Select images"
            Html.input [
                input.type' "file"
                prop.multiple true
                prop.hidden true
                prop.onChange (fun (e: Event) ->
                    let files: File array = e.target?files
                    handleFilesSelected files
                )
            ]
        ]
        TagsSenseShapeForm (Sense.empty ()) tagsSenseChanged
        match files with
        | FilesState.Empty -> ()
        | FilesState.Selected files ->
            Mui.box @+ [
                prop.sx {|
                    display = "flex"
                    flexWrap = "wrap"
                    flexDirection = "row"
                    justifyContent = "center"
                    gap = 1
                |}
            ] <| [
                for file in files ->
                    PreviewImage file
            ]
            Mui.button [
                prop.text "Batch create"
                prop.onClick (fun _ -> handleBatchCreateClicked files)
            ]
    ]
