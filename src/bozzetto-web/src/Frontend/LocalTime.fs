// Local wall-clock presentation of an instant, shared by every view and the
// frontend tests. Times are shown as the instants they are, never relative to
// "now": a relative time would need a ticking clock, and the page keeps none.
module Bozzetto.Web.Frontend.LocalTime

open Fable.Core

// Local time alone on the reference's local calendar day ("9:03:12 PM");
// a short local date and time on any other day of the same local year
// ("Nov 5, 9:03 PM"); the year as well on a day of another year
// ("Nov 5, 2025, 9:03 PM"). A non-positive reference means now. Some ICU
// builds separate the time from its day period with a narrow no-break space;
// it is shown as an ordinary space so every engine prints the same text.
[<Emit("(() => { const d = new Date($0); const ref = new Date($1 > 0 ? $1 : Date.now()); let shown; if (d.getFullYear() === ref.getFullYear() && d.getMonth() === ref.getMonth() && d.getDate() === ref.getDate()) shown = d.toLocaleTimeString(); else { const day = d.getFullYear() === ref.getFullYear() ? { month: 'short', day: 'numeric' } : { month: 'short', day: 'numeric', year: 'numeric' }; shown = d.toLocaleDateString([], day) + ', ' + d.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' }); } return shown.replace(/\\u202f/g, ' '); })()")>]
let localTimestampAt (epochMs: float) (referenceMs: float) : string = jsNative

// The single local-zone format used across every view.
let clockTime (epochMs: float) : string = localTimestampAt epochMs 0.

let dateTime (epochMs: float) : string = localTimestampAt epochMs 0.
