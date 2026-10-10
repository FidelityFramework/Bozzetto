/// Linux acquisition edge and pure interval calculations. No timer, policy,
/// subprocess, managed process object, or kill authority lives here.
module Bozzetto.Server.LinuxResources

open System
open System.IO
open System.Globalization
open System.Diagnostics
open Bozzetto.Web.Shared.Protocol

type Cpu = { Total: int64; Idle: int64; Wait: int64; Count: int }
type ProcessCounter = {
  Pid: int; Parent: int; Start: int64; Name: string
  Ticks: int64; Resident: int64; Threads: int
}
type Raw = {
  At: int64; Clock: int64; Cpu: Cpu option; Processes: ProcessCounter array
  Metrics: ResourceMetric array; SwapIn: int64 option; SwapOut: int64 option
  Count: int; Unreadable: int; Truncated: bool
}

let private fields (text: string) = text.Split([|' '; '\t'; '\n'; '\r'|], StringSplitOptions.RemoveEmptyEntries)
let private integer (text: string) =
  match Int64.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture) with
  | true, n when n >= 0L -> Some n
  | _ -> None
let private number (text: string) =
  match Double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture) with
  | true, n when Double.IsFinite n && n >= 0. -> Some n
  | _ -> None
let private read path = try Some(File.ReadAllText path) with _ -> None
let private directories path =
  try Directory.EnumerateDirectories path |> Seq.truncate 8193 |> Seq.toArray
  with _ -> [||]
let private metric name unit value : ResourceMetric = { Name = name; Unit = unit; Value = value }

/// Guest times are already in user/nice. Include the first eight fields only.
let parseCpu (text: string) =
  let lines = text.Split('\n')
  lines |> Array.tryFind (fun l -> l.StartsWith "cpu ") |> Option.bind (fun line ->
    let xs = fields line |> Array.skip 1 |> Array.truncate 8 |> Array.map integer
    if xs.Length < 8 || Array.exists Option.isNone xs then None
    else
      let n = Array.map Option.get xs
      Some { Total = Array.sum n; Idle = n[3]; Wait = n[4]
             Count = lines |> Array.filter (fun l -> l.Length > 3 && l.StartsWith "cpu" && Char.IsDigit l[3]) |> Array.length })

/// comm may contain spaces and closing parentheses; numeric fields follow the
/// last ')'. RSS is pages; utime+stime excludes separately reported child time.
let parseProcess pageSize (text: string) =
  try
    let left, right = text.IndexOf('('), text.LastIndexOf(')')
    if left < 1 || right < left then None
    else
      let xs = fields (text.Substring(right + 1))
      let n at = Int64.Parse(xs[at], CultureInfo.InvariantCulture)
      let pid = Int32.Parse(text.Substring(0, left).Trim(), CultureInfo.InvariantCulture)
      let ticks, start, rss = n 11 + n 12, n 19, n 21
      if ticks < 0L || start < 0L || rss < 0L then None
      else Some { Pid = pid; Parent = int (n 1); Start = start; Name = text.Substring(left + 1, right - left - 1)
                  Ticks = ticks; Resident = rss * int64 pageSize; Threads = int (n 17) }
  with _ -> None

let parseValues (text: string) =
  text.Split('\n') |> Array.choose (fun line ->
    match fields line with
    | xs when xs.Length >= 2 -> integer xs[1] |> Option.map (fun n -> xs[0].TrimEnd(':'), n)
    | _ -> None) |> Map.ofArray

let cpuDelta (previous: Cpu) (current: Cpu) =
  let total = current.Total - previous.Total
  let idle, wait = current.Idle - previous.Idle, current.Wait - previous.Wait
  if total <= 0L || idle < 0L || wait < 0L || idle + wait > total || previous.Count <> current.Count then None
  else Some(100. * float (total - idle - wait) / float total, 100. * float wait / float total, total)

let processPercent totalDelta cpus (previous: ProcessCounter option) (current: ProcessCounter) =
  previous |> Option.bind (fun p ->
    if totalDelta <= 0L || p.Pid <> current.Pid || p.Start <> current.Start || current.Ticks < p.Ticks then None
    else Some(100. * float (current.Ticks - p.Ticks) / float totalDelta * float cpus))

let private gpuMetrics () =
  let cards = directories "/sys/class/drm" |> Array.filter (fun p ->
    let name = Path.GetFileName p
    name.StartsWith "card" && name.Length > 4 && name.Substring(4) |> Seq.forall Char.IsDigit)
  if cards.Length = 0 then [| metric "GPU utilization (unsupported)" "percent" None |]
  else cards |> Array.collect (fun card ->
    let device = Path.Combine(card, "device")
    let name = Path.GetFileName card
    let get file = read (Path.Combine(device, file)) |> Option.bind (fun s -> number (s.Trim()))
    [| metric (name + " GPU busy") "percent" (get "gpu_busy_percent")
       metric (name + " driver VRAM used (may share RAM)") "bytes" (get "mem_info_vram_used")
       metric (name + " driver VRAM budget") "bytes" (get "mem_info_vram_total")
       metric (name + " driver GTT used (host backing)") "bytes" (get "mem_info_gtt_used") |])

let acquire () =
  let memory = read "/proc/meminfo" |> Option.map parseValues |> Option.defaultValue Map.empty
  let mem key = Map.tryFind key memory |> Option.map (fun n -> float n * 1024.)
  let used total available =
    Option.map2 (fun t a -> t,a) (mem total) (mem available)
    |> Option.bind (fun (t,a) -> if a <= t then Some(t-a) else None)
  let vm = read "/proc/vmstat" |> Option.map parseValues |> Option.defaultValue Map.empty
  let paths =
    try
      Directory.EnumerateDirectories "/proc"
      |> Seq.filter (fun p -> Path.GetFileName p |> Seq.forall Char.IsDigit)
      |> Seq.truncate 8193 |> Seq.toArray
    with _ -> [||]
  let rows = paths |> Array.truncate 8192 |> Array.choose (fun p -> read (Path.Combine(p,"stat")) |> Option.bind (parseProcess Environment.SystemPageSize))
  let pressure kind =
    read ("/proc/pressure/" + kind) |> Option.bind (fun text ->
      text.Split('\n') |> Array.tryFind (fun l -> l.StartsWith "some ") |> Option.bind (fun line ->
        fields line |> Array.tryFind (fun s -> s.StartsWith "avg10=") |> Option.bind (fun s -> number (s.Substring 6))))
  { At = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); Clock = Stopwatch.GetTimestamp()
    Cpu = read "/proc/stat" |> Option.bind parseCpu; Processes = rows
    SwapIn = Map.tryFind "pswpin" vm; SwapOut = Map.tryFind "pswpout" vm
    Count = paths.Length; Unreadable = min 8192 paths.Length - rows.Length; Truncated = paths.Length > 8192
    Metrics = Array.concat [
      [| metric "RAM used (total − available)" "bytes" (used "MemTotal" "MemAvailable")
         metric "RAM available" "bytes" (mem "MemAvailable")
         metric "RAM total (OS managed)" "bytes" (mem "MemTotal")
         metric "File cache" "bytes" (mem "Cached")
         metric "Swap used" "bytes" (used "SwapTotal" "SwapFree")
         metric "Swap total" "bytes" (mem "SwapTotal")
         metric "Memory stalls (PSI some, avg10)" "percent" (pressure "memory")
         metric "CPU stalls (PSI some, avg10)" "percent" (pressure "cpu")
         metric "I/O stalls (PSI some, avg10)" "percent" (pressure "io") |]
      gpuMetrics () ] }

let project (previous: Raw option) (current: Raw) : HostResources =
  let interval = previous |> Option.map (fun p -> Stopwatch.GetElapsedTime(p.Clock,current.Clock).TotalMilliseconds) |> Option.defaultValue 0.
  let delta = Option.bind (fun p -> Option.bind (fun old -> current.Cpu |> Option.bind (cpuDelta old)) p.Cpu) previous
  let oldProcesses = previous |> Option.map (fun p -> p.Processes |> Array.map (fun p -> p.Pid,p) |> Map.ofArray) |> Option.defaultValue Map.empty
  let processes = current.Processes |> Array.map (fun p ->
    { ResourceProcess.ProcessId = p.Pid; ParentId = p.Parent; StartTicks = string p.Start; Name = p.Name; Context = "external/unattributed"
      CpuPercent = delta |> Option.bind (fun (_,_,total) -> processPercent total (current.Cpu.Value.Count) (Map.tryFind p.Pid oldProcesses) p)
      ResidentBytes = float p.Resident; Threads = p.Threads })
  let selected = Array.append
                   (processes |> Array.sortByDescending (fun p -> Option.defaultValue -1. p.CpuPercent) |> Array.truncate 12)
                   (processes |> Array.sortByDescending _.ResidentBytes |> Array.truncate 12)
                 |> Array.distinctBy _.ProcessId
  let swap previousValue currentValue =
    Option.bind (fun p -> Option.map2 (fun old now -> old,now) (previousValue p) currentValue) previous
    |> Option.bind (fun (old,now) -> if now < old || interval <= 0. then None else Some(float (now-old) * float Environment.SystemPageSize * 1000. / interval))
  { SampledAtMs = current.At; IntervalMs = interval; SampleCostMs = 0.
    Sequence = 0L; Status = "observing"
    Metrics = Array.append
      [| metric "Host CPU busy" "percent" (delta |> Option.map (fun (busy,_,_) -> busy))
         metric "Host CPU I/O wait" "percent" (delta |> Option.map (fun (_,wait,_) -> wait))
         metric "Logical CPUs" "count" (current.Cpu |> Option.map (fun c -> float c.Count))
         metric "Swap in" "bytes/s" (swap _.SwapIn current.SwapIn)
         metric "Swap out" "bytes/s" (swap _.SwapOut current.SwapOut) |] current.Metrics
    Processes = selected; ProcessCount = current.Count; UnreadableCount = current.Unreadable
    OmittedCount = max 0 (current.Processes.Length - selected.Length)
    Note = "Linux host scope; GPU driver counters overlap host RAM on UMA—do not add them. RSS includes shared pages. First CPU/rate reading needs a second observation. "
           + (if current.Truncated then "Process scan capped at 8192. " else "")
           + "No lease attribution, leak verdict or automatic termination in this slice." }
