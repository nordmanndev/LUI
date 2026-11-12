# The documentation of LUI

LUI's documentation has three jobs: a **way** that shows how to use the library, a **reference** that says what there
is, and a **record** that says what it costs. The [README](../README.md) is the entrance; this file is the map.

## The map

| Document | What it is | Read it when |
|---|---|---|
| [The tutorial](./tutorial/README.md) | the **way**: five steps from an empty database to a working autocomplete | you want to build something |
| [The API](./api.md) | every public type of the package, with a sentence for each | you want to know what you can call |
| [The benchmarks](./benchmarks.md) | every measured number, with the machine, the corpus and the method | you need a number you can cite |
| [The normalisation](./normalization.md) | the rule that turns a text into a searchable form — and what was measured about it | you touch the fold rule or the parity |
| [The test map](./testing.md) | which test proves which rule — a guard, not a document | you change a rule and need its test |
| [What applies to a library](./architecture/alignment.md) | the engineering hygiene of a library: the rules that apply here, and the ones that describe an application and do not | you wonder why the library is built the way it is |
| [The domain](./domain/lui.md) | the language, the invariants, the data model — the context behind the contract | you want the vocabulary |

## Who reads what

| Reader | Reads |
|---|---|
| **a user of the package** | the [tutorial](./tutorial/README.md), then the [API](./api.md) for the details |
| **a contributor** | [`AGENTS.md`](../AGENTS.md) — the rules of the repository — then the [test map](./testing.md) and [what applies to a library](./architecture/alignment.md) |
| **someone with a number** | the [benchmarks](./benchmarks.md) — a number without a machine is a rumour |
| **a reviewer** | the [diff of the pull request](https://github.com/nordmanndev/LUI/pulls) — the decisions of the work are papers of the work, not documentation of the package |

## The rules of the map

- **One truth, one place.** A rule is stated once: in the code, or in a paper of the work — not in both, and not in the
  README.
- **The documentation of the library is about the library.** The decisions of the work, the delivery plans and the
  specification are papers of the work: they are not part of this documentation, and nothing here links them.
- **A document nobody links to is a document nobody reads.** Every file of this directory is reachable from the
  [README](../README.md) or from here — directly or through the index of its own folder.

The two files in [`./images/`](./images/) are the screenshots and the film of the [sample application](../samples/Lui.Sample/)
that the [README](../README.md) shows — assets, not documents.
