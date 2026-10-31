---
name: mdd
description: Generate code from an M3L data model with the mdd CLI — SQL schema, EF Core entities and DbContext, OData/GraphQL registration and TypeScript types and forms — driven by one mdd.json. Use when asked to add or change an entity, field or index and regenerate its code, to wire a new generation target, to find out why `mdd build` stops, or to move a model rename through to the database.
---

# mdd: from an M3L model to generated code

`mdd build` reads one model (M3L, `*.m3l.md`) and the `mdd.json` beside it, and writes code into the projects the
targets name. You change the **model** and rebuild; you never edit what it generated.

## Prerequisites

`mdd` on PATH (`dotnet tool install -g mdd`). `mdd.json` sits in a directory of its own; `mdd build [<config-dir>]`
runs it (current directory when omitted). The model syntax is the M3L specification; this skill covers the loop around
the generator. Configuration keys, per-target options and the diagnostic catalog are in the README — read the section for
the target you are touching.

## The loop

1. **Edit the model** (`tables.m3l.md` or whatever `sources` lists). A new column is a new field line; a new index is an
   `### Indexes` entry or an attribute.
2. **Build**: `mdd build ./path/to/config-dir`. Read the exit code:
   - `0` — generated; warnings, if any, were printed.
   - `3` — the model is wrong: parser or analysis **errors**, or warnings you promoted to errors. Nothing is generated.
   - `4` — `mdd.json` is wrong (unknown option on a target, a bad path, a warning-policy key that is not a code).
   - `2` — the config directory does not exist; `1` — bad command line, or an output could not be written (some files of
     that target may have been).
3. **Fix by code.** Every diagnostic is printed as `[MDDnnn]` (this generator) or `[M3L-Ennn]` / `[M3L-Wnnn]` (the parser)
   with `file:line:col`. The catalog is the README's diagnostics table; branch on the code, not the wording. A code means
   one condition.
4. **Review the generated diff** in the target projects (`git diff` there) — the generated file names end in `_gen`. They
   are rewritten on every build.
5. **Apply the SQL** to the database with whatever the project uses for schema changes; `mdd` writes `.sql` files and
   does not touch a database.

## Targets

`targets` in `mdd.json` is a list; each entry has a `type`:

| type | produces |
|---|---|
| `Sql` | per-entity table and view scripts (SQL Server by default, `"dialect": "postgres"` for PostgreSQL) |
| `Model` | entity classes, EF Core mappings, enums, the DbContext |
| `Api` | OData + GraphQL entity registration |
| `TypeScript` | types, enum labels, field schemas, and forms when `formsOutputPath` is set |

Two targets of one kind may not write to the same path (a build error). An option placed at the wrong level is caught
when the config declares `$schema` — keep that line.

## Warnings

Warnings do not fail a build. For a CI gate, set `treatWarningsAsErrors` (or `warningsAsErrors` / `noWarn`) **in
`mdd.json`** and rely on exit `3`; do not grep the console text.

## Renames

A model or field renamed in the model reads as "drop the old, create the new" to every schema tool. Declare it with
`@renamed_from(OldName)` and give the `Sql` target a `preScriptsPath`; without that path the build warns (`MDD051`)
because the rename would be applied as a data-losing drop and re-create.

## Pitfalls

- **Do not edit `*_gen` files** or patch generated code to make it compile — a compile error in generated code is a model
  or configuration fix (or a generator defect to report), and the next build overwrites the patch.
- **Generated code uses simple type names.** The consuming project resolves them with its own global usings (the runtime
  package's README lists the lines it needs). Do not add `using` lines to generated files.
- **A composite primary key is refused** (`MDD055`): model the table with one `identifier` key and guard the pair with a
  composite `@unique(a, b)`.
- **A short name must be unique in a build** (`MDD053`), even across namespaces.
- The CLI has no machine-readable output mode; the exit code and the coded stderr lines are the contract.
