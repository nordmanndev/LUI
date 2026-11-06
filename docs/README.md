# The documentation of LUI

LUI's documentation has three jobs: a **contract** that says what the library must do, a **way** that shows how to
use it, and a **history** that says why it looks the way it does. The [README](../README.md) is the entrance; this
file is the map.

## The map

| Document | What it is | Read it when |
|---|---|---|
| [The tutorial](./tutorial/README.md) | the **way**: five steps from an empty database to a working autocomplete | you want to build something |
| [The specification](./spec/LUI-Spezifikation.md) | the **contract**: the kinds of index, the sources of the content, the query path, the ranking, the typo tolerance, the API | you want to know what the library must do |
| [The decisions](./adr/README.md) | the **history** of the decisions: 36 ADRs, one per decision, each with its reason | you wonder why it is like that |
| [The delivery plans](./plan/README.md) | the **history** of the work: which plan brought what, and when | you want to know how the repository got here |
| [The benchmarks](./benchmarks.md) | every measured number, with the machine, the corpus and the method | you need a number you can cite |
| [The normalisation](./normalization.md) | the rule that turns a text into a searchable form — and what was measured about it | you touch the fold rule or the parity |
| [The test map](./testing.md) | which test proves which rule — a guard, not a document | you change a rule and need its test |
| [The alignment](./architecture/alignment.md) | which rules of `nordmanndev/standards` apply to a library, and which do not | you wonder why the standards are not applied literally |
| [The domain](./domain/lui.md) | the language, the invariants, the data model — the context behind the contract | you want the vocabulary |

## Who reads what

| Reader | Reads |
|---|---|
| **a user of the package** | the [tutorial](./tutorial/README.md), then the [specification](./spec/LUI-Spezifikation.md) for the details |
| **a contributor** | [`AGENTS.md`](../AGENTS.md) — the contract of the repository — then the [test map](./testing.md) and the [alignment](./architecture/alignment.md) |
| **a reviewer** | the [ADRs](./adr/README.md): the decision and its reason; the [plans](./plan/README.md) for the history |
| **someone with a number** | the [benchmarks](./benchmarks.md) — a number without a machine is a rumour |

## The rules of the map

- **One truth, one place.** A rule is stated once: in the specification, or in an ADR — not in both, and not in the
  README.
- **The specification is the contract.** Code that contradicts it is a bug in the code, unless an ADR says the
  specification is wrong; then both change in the same pull request ([`AGENTS.md`](../AGENTS.md)).
- **An ADR is a decision, not a document.** Context, decision, consequences — the [index](./adr/README.md) finds it
  by its topic.
- **A document nobody links to is a document nobody reads.** Every file of this directory is reachable from the
  [README](../README.md) or from here — directly or through the index of its own folder.

The two files in [`./images/`](./images/) are the screenshots of the [sample application](../samples/Lui.Sample/) that
the [README](../README.md) shows — assets, not documents.
