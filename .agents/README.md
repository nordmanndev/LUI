# The papers of the work

> The internal documents of this repository: the decisions, the delivery plans and the specification. They are **not**
> part of the documentation of the library — a consumer of the package has no business in them. They live here, under
> `.agents/`, and the [contract](../AGENTS.md) of the repository points at them.

| Paper | What it is |
|---|---|
| [The specification](./spec/LUI-Spezifikation.md) | the contract of the library: the kinds of index, the sources of the content, the query path, the ranking, the typo tolerance, the API. The implementation follows it; where the code and the specification disagree, the specification wins and the difference becomes an ADR. |
| [The decisions](./adr/README.md) | one ADR per decision, with its reason. The index finds them by their topic. |
| [The delivery plans](./plan/README.md) | one plan per piece of work, with the steps and their acceptance criteria. The history of the repository. |
| [The state](./state/) | the note of the session in flight: what is done, what is open, what the next step is. |

## The rules of the papers

- **A decision is an ADR, and it is written in the pull request that makes it.** A change of the public API, of the
  schema or of the dependency set needs one — see the *Never* rules of [`AGENTS.md`](../AGENTS.md).
- **A changed decision gets a new ADR.** A record that is superseded stays and says so; a record that is *restated* —
  the same decision, sharper — is marked in the index of the decisions.
- **A plan is written before the work.** It names the steps, their acceptance criteria and the ADR they will produce.
- **The papers are not published.** The documentation of the library is in [`../docs/`](../docs/README.md): the
  entrance, the way, the API and the numbers. A reader of the package reads those.
