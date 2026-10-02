module Bozzetto.Server.ComposerRoutes

open System
open System.IO
open Fidelity.Data.JSON
open System.Threading.Channels
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Bozzetto.ComposerIntegration

let private framingError = ComposerClientJson.framingError

let private writeJson (ctx: HttpContext) status (response: JsonValue) = task {
  ctx.Response.StatusCode <- status
  ctx.Response.ContentType <- "application/json; charset=utf-8"
  do! ctx.Response.WriteAsync(Json.serialize response, ctx.RequestAborted)
}

let internal readBody (ctx: HttpContext) = task {
  let maximum = 1024 * 1024
  if ctx.Request.ContentLength.HasValue && ctx.Request.ContentLength.Value > int64 maximum then
    raise (InvalidDataException "Composer request exceeds 1 MiB.")
  use content = new MemoryStream()
  let buffer = Array.zeroCreate<byte> 16384
  let mutable reading = true
  while reading do
    let! count = ctx.Request.Body.ReadAsync(buffer.AsMemory(), ctx.RequestAborted)
    if count = 0 then reading <- false
    elif content.Length + int64 count > int64 maximum then
      raise (InvalidDataException "Composer request exceeds 1 MiB.")
    else content.Write(buffer, 0, count)
  try
    let utf8 = System.Text.UTF8Encoding(false, true)
    return content.ToArray() |> utf8.GetString |> Json.parse
  with :? System.Text.DecoderFallbackException as error -> return Result.Error error.Message
}

let private page = """<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Composer · Bozzetto</title><style>
:root{color-scheme:dark;font:16px system-ui,sans-serif;background:#111820;color:#e5edf5}
body{max-width:1100px;margin:0 auto;padding:2rem}h1{margin-bottom:.3rem}p{line-height:1.5;color:#b6c6d6}
section,article{background:#18232e;border:1px solid #354658;border-radius:9px;padding:1rem;margin:1rem 0}
input{background:#101820;color:inherit;border:1px solid #526579;border-radius:4px;padding:.65rem;min-width:15rem;flex:1}
button{background:#295a75;border:1px solid #4b849e;color:#fff;border-radius:4px;padding:.65rem;cursor:pointer}
button:disabled{opacity:.45;cursor:default}.row{display:flex;gap:.6rem;flex-wrap:wrap;align-items:center}
pre{white-space:pre-wrap;overflow-wrap:anywhere;background:#101820;padding:1rem;border-radius:5px;font-size:.85rem}
small{color:#b6c6d6}h2{font-size:1.15rem}#connection{font-weight:600}a{color:#84cce9}
</style></head><body>
<h1>Composer development</h1><p>Clef projects, compiler evidence, and execution shared with Bozzetto's agent tools.</p>
<p><strong>Reserve before editing.</strong> Wait for reservation success, edit source or dependencies in your editor, then build that reservation. Run always revalidates inputs and executable bytes.</p>
<section><div class="row"><input id="project" aria-label="Absolute project path" placeholder="/absolute/path/Project.fidproj"><button id="open">Open project</button><button id="refresh">Refresh</button></div>
<p id="connection">Loading provider status…</p><div class="row"><label for="arguments">Run arguments (JSON array)</label><input id="arguments" value="[]"><button id="retire">Retire compiler worker</button></div>
<small>Retire before replacing compiler binaries. Cleanup failures remain visible; reopening uses a fresh worker epoch. Active compiler patching is unsupported.</small></section>
<main id="sessions"></main><section><h2>Latest operation and evidence</h2><pre id="result">Open a project to begin.</pre></section>
<script>
const byId=id=>document.getElementById(id), reservations=new Map();let snapshot=null;
const identityKey=a=>[a.host,a.session,a.epoch].join('/');
const show=value=>{byId('result').textContent=JSON.stringify(value,null,2)};
async function request(operation,body){const response=await fetch('/api/composer/'+operation,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)});return await response.json()}
function button(label,action,disabled=false){const el=document.createElement('button');el.textContent=label;el.disabled=disabled;el.onclick=async()=>{el.disabled=true;try{await action()}catch(error){show({error:String(error)})}finally{await refresh()}};return el}
async function act(operation,authority,extra={}){const result=await request(operation,{host:authority.host,session:authority.session,epoch:authority.epoch,...extra});show(result);return result}
async function refresh(){try{
  const response=await fetch('/api/composer/sessions');snapshot=await response.json();
  byId('connection').textContent=snapshot.configured?'Composer provider configured · shared state revision '+snapshot.revision:'Composer worker is not configured. Set BOZZETTO_COMPOSER_WORKER before starting the daemon.';
  byId('retire').disabled=!(snapshot.worker&&snapshot.worker.authority);
  const root=byId('sessions');root.replaceChildren();
  for(const envelope of snapshot.sessions||[]){const authority=envelope.authority,status=envelope.result||{},key=identityKey(authority),card=document.createElement('article');
    const title=document.createElement('h2');title.textContent=status.project||authority.session;card.append(title);
    const identity=document.createElement('p');identity.textContent='Session '+authority.session+' · epoch '+authority.epoch+' · revision '+authority.generation+(status.closed?' · closed':status.busy?' · busy':'');card.append(identity);
    const actions=document.createElement('div');actions.className='row';
    actions.append(button('Status',async()=>{await act('status',authority)}));
    actions.append(button('Reserve edit',async()=>{const label=prompt('Describe this edit','source edit');if(label===null)return;const response=await act('reserve',authority,{label});reservations.delete(key);if(response.success)reservations.set(key,response.result.reservation)},status.closed));
    actions.append(button('Build reserved revision',async()=>{const reservation=reservations.get(key);reservations.delete(key);await act('build',authority,{reservation})},status.closed||!reservations.has(key)));
    actions.append(button('Run current',async()=>{const args=JSON.parse(byId('arguments').value);if(!Array.isArray(args)||args.some(x=>typeof x!=='string'))throw Error('Run arguments must be an array of strings.');await act('run',authority,{arguments:args})},status.closed||!status.current));
    actions.append(button('Cancel',async()=>{reservations.delete(key);await act('cancel',authority)},status.closed));
    actions.append(button('Close',async()=>{reservations.delete(key);await act('close',authority)}));card.append(actions);
    const evidence=document.createElement('pre');evidence.textContent=JSON.stringify(envelope,null,2);card.append(evidence);root.append(card);
  }
  if(!(snapshot.sessions||[]).length){const empty=document.createElement('p');empty.textContent='No Composer sessions are open.';root.append(empty)}
}catch(error){byId('connection').textContent='Provider status unavailable: '+String(error)}}
byId('open').onclick=async()=>{try{show(await request('open',{project:byId('project').value}));await refresh()}catch(error){show({error:String(error)})}};
byId('refresh').onclick=refresh;
byId('retire').onclick=async()=>{if(!snapshot?.worker?.authority)return;try{await act('prepare_compiler_change',snapshot.worker.authority);reservations.clear();await refresh()}catch(error){show({error:String(error)})}};
const events=new EventSource('/api/composer/events');events.onmessage=refresh;
refresh();
</script></body></html>"""

let mapRoutes (app: WebApplication) (supervisor: ComposerSupervisor) =
  app.MapGet("/composer", RequestDelegate(fun (ctx: HttpContext) -> task {
    ctx.Response.ContentType <- "text/html; charset=utf-8"
    do! ctx.Response.WriteAsync(page, ctx.RequestAborted)
  })) |> ignore

  app.MapGet("/api/composer/sessions", RequestDelegate(fun (ctx: HttpContext) -> task {
    let! response = supervisor.SessionsAsync ctx.RequestAborted
    do! writeJson ctx 200 (ComposerClientJson.directory response)
  })) |> ignore

  app.MapPost("/api/composer/{operation}", RequestDelegate(fun (ctx: HttpContext) -> task {
    try
      let! parsed = readBody ctx
      match parsed with
      | Result.Error message -> do! writeJson ctx 400 (framingError "invalid_json" message)
      | Result.Ok body ->
        match ComposerClientJson.request (string ctx.Request.RouteValues["operation"]) body with
        | Result.Error(code, message) -> do! writeJson ctx 400 (framingError (ComposerClientJson.refusalCode code) message)
        | Result.Ok request ->
          let! response = supervisor.ExecuteAsync(request, ctx.RequestAborted)
          do! writeJson ctx 200 (ComposerClientJson.reply response)
    with
    | :? InvalidDataException as error -> do! writeJson ctx 413 (framingError "request_too_large" error.Message)
  })) |> ignore

  // The browser and MCP resource subscriptions observe the same change signal.
  // Events are invalidation hints; the shared status query supplies authority.
  app.MapGet("/api/composer/events", RequestDelegate(fun (ctx: HttpContext) -> task {
    ctx.Response.ContentType <- "text/event-stream"
    ctx.Response.Headers["Cache-Control"] <- "no-cache"
    let options = BoundedChannelOptions(1, FullMode = BoundedChannelFullMode.DropOldest)
    let events = Channel.CreateBounded<unit> options
    use subscription = supervisor.Changed.Subscribe(fun () -> events.Writer.TryWrite() |> ignore)
    events.Writer.TryWrite() |> ignore
    try
      let mutable listening = true
      while listening do
        let! available = events.Reader.WaitToReadAsync(ctx.RequestAborted)
        if not available then listening <- false
        else
          let mutable ignored = ()
          while events.Reader.TryRead(&ignored) do ()
          do! ctx.Response.WriteAsync("data: changed\n\n", ctx.RequestAborted)
          do! ctx.Response.Body.FlushAsync(ctx.RequestAborted)
    with :? OperationCanceledException -> ()
  })) |> ignore
