/// The scenario registry (Island F, demo-actors-plan.md §1.3): aggregates
/// every client's scenario list into the one `all` value `Program.fs`
/// resolves `record <scenario-id>` against. Each actor island owns and fills
/// only its own `Scenarios.<X>.scenarios` stub; after Island F, no actor
/// island ever touches this file again.
module Bozzetto.Demos.Scenarios.All

let all: Bozzetto.Demos.Domain.Scenario list =
  Bozzetto.Demos.Scenarios.Dashboard.dashboardScenarios
  @ Bozzetto.Demos.Scenarios.VsCode.scenarios
  @ Bozzetto.Demos.Scenarios.Neovim.scenarios
  @ Bozzetto.Demos.Scenarios.Agent.scenarios
  @ Bozzetto.Demos.Scenarios.Cohort.scenarios
