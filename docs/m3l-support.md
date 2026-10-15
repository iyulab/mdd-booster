# M3L support in mdd-booster

> **Specification:** the M3L language is defined upstream, in the `m3l` project. This generator pins
> **`M3L.Native` 0.21.0**; the specification at that version is
> [`docs/specification.md` @ 0.21.0](https://github.com/iyulab/m3l/blob/457ce211f8fca1a84cfeb32ecbdca8feafcf12c8/docs/specification.md).
> Section numbers below are that document's.

This page answers the question a reader of the specification brings here: **where this generator
stops.** A section of the specification that the generator acts on only in part — or not at all —
has a note below, under the section's number and title. **A section
without a note is implemented as written.**

Read the note before building on a section. Almost everything in the specification parses, and
almost nothing fails a build for being unsupported. A `### Section` the language does not define,
and a declaration no target reads, are reported as unconsumed at build time; an *attribute* the
generator ignores is not, so it looks exactly like one it implements right up until the output is
missing. The notes that do fail a build say so.

This page carries notes only. It used to be a full copy of the specification with the notes inside
it, and the copy drifted from its source — section numbers moved upstream and the copy kept the old
ones. Keying every note to the pinned specification's own numbering, and linking that version, is
what keeps the two from disagreeing.

## Attributes no target reads

The paragraph above names the gap; this names the attributes that fall into it.
**What this generator implements** for each of them is *nothing* — the attribute parses, reaches
the model, and no target emits anything from it. Nothing is reported at build time either, which is
what makes them worth listing in one place.

| Attribute | The specification defines it for | Where the same intent does reach the output |
|---|---|---|
| `@min_length` · `@max_length` | length bounds on a string field | the declared type — `string(20)` sets the column width, the API surface and the generated form together |
| `@pattern` | a regular expression a value must match | nowhere; no check is emitted and none runs at build time |
| `@ledger` | a table that only ever receives inserts | nowhere; no constraint, trigger or comment is emitted |
| `@materialized` | a view whose rows are stored rather than computed per query | nowhere. The SQL target generates standalone `::view` models, but not a materialized one — that view is left out, and the build says so (`MDD031`) |

Two attributes are absent from this list on purpose. `@validate` and `@behavior` are equally unread,
but each has a section of its own below (§4.10, §4.3) and its own note there; repeating them here
would give the same fact two homes that can disagree.

The list is measured, not remembered: a model declaring all five produces SQL, entity and API output
in which none of the five appears anywhere, and a test asserts exactly that. If one of them starts
being implemented, that test fails and this table is what it points at.

## §2.3.1.1 Shortened Type Notations

**What this generator implements** differs from the specification's table in two places, and both
are deliberate:

- `phone` maps to a **30**-character bound, not 20. Narrowing an existing column is not
  a change a generator can make on a consumer's behalf — stored values longer than the
  new bound make a migration fail or truncate, and the generator cannot see the stored
  data. The deviation is recorded in code and fixed by a test, and it is *wider* than
  the specification, so no value the specification permits is refused.
- `money` and `percentage` are **not implemented**. The SQL type mapper has no case for
  them and raises an unsupported-type error, so a model declaring either fails the build
  rather than generating something approximate. Write `decimal(19,4)` or `decimal(5,2)`
  explicitly.

No format validation is emitted for any of these types — the bound is the length axis
only. A 321-character value is refused; `not-an-email` is not.

## §2.5.6 Custom Framework Attributes

**What this generator implements** — none of them. A custom attribute parses and reaches the model,
and no target carries it into the output: this generator owns how a model becomes C#, SQL and
TypeScript, and a raw platform attribute would bypass that mapping and tie the model to one target.
The build lists each one among the unconsumed elements (`[m3l] 경고: 소비되지 않는 요소`) rather than
dropping it silently. Where the intent has M3L vocabulary, use that — a row version is the
`rowversion` type (§10.4.1), which every target maps.

## §3.1.3 Enum Definition with Value Types

**What this generator implements**: the members and their labels, not the numbers. An explicit value
is read by no target, and the axis is inert rather than merely unimplemented: an enum persists as its
declared *name* — EF converts it to a string, and the opt-in SQL CHECK constraint lists the same text
— so a declared number has nowhere to land. The generated C# member takes its ordinal instead, which
is not the number written here. Write the value type and number for a reader if you like; nothing
downstream will disagree with it, because nothing downstream reads it.

## §3.1.6 Enum Inheritance

**What this generator implements**: implemented, on every target. The parser joins the parent's
values into the child's own — parent's first, then the child's — so the enum generated for a child
holds the parent's members and then its own, in that order, in the C# enum, the TypeScript union and
— where that opt-in is on — the SQL CHECK literal alike. The order decides the C# ordinals; the
stored value is the member's name, so reordering never changes what a row holds.

## §3.2.1.1 CASCADE Behavior

**What this generator implements**: the three symbols, on both SQL dialects — `!` writes
`ON DELETE NO ACTION`, `?` writes `ON DELETE SET NULL` (on a key that cannot hold NULL the build
stops with MDD023), and `!!` writes `ON DELETE RESTRICT` on PostgreSQL and `ON DELETE NO ACTION` on
SQL Server, which has no RESTRICT and checks NO ACTION immediately. The automatic decision the section
describes is **not** applied: a reference without a symbol writes no `ON DELETE` clause, so the
database default (NO ACTION) holds. Applied as written, it would make common models undeployable on
SQL Server — a self-reference, or two keys to the same model, cannot both cascade or set null there
(error 1785). The same limit applies to the symbols as written, so the SQL Server dialect stops the
build on a set of `?` keys (with the cascading key an `::aspect` makes) that SQL Server would refuse —
a `?` self-reference, two `?` keys from one model to another, or a delete that would reach one table
twice or come back to its own. A SET NULL ends at the rows it updates, so `?` keys of different
models that form a loop or meet on one table are accepted. PostgreSQL has no such limit and writes
them all. The attribute forms (`@cascade`, `@no_action`, `@set_null`, `@restrict`, `@cascade(X)`) are
deprecated by the parser (M3L-W003) and are not read. The Model target (EF Core) configures the same
action on the write entity's navigation (`OnDelete(DeleteBehavior.NoAction | SetNull | Restrict)`),
so EF does not delete tracked rows the database was told to keep; a key with no symbol keeps EF's
convention. With `!` or `!!` on a required key, removing a row whose referencing rows are tracked by
the same context throws at `Remove()` — EF's message then suggests configuring a cascade delete,
which is the opposite of what the symbol declares. Delete or detach those rows first; an untracked
reference is refused by the database when the delete is saved.

## §3.2.5 Relationship Attributes

**What this generator implements**: the `@reference(Target)` attribute and the delete action a key
declares — the symbol after it (`!` `?` `!!`) or `on_delete` (`- on_delete: cascade` in the extended
format, or `@on_delete(cascade)`; `cascade` · `set_null` · `restrict` · `no_action`). Foreign keys,
navigation properties and lookup redirects are all built from the reference; a symbol and an
`on_delete` that disagree stop the build (`MDD046`). `on_update` is not read.

From `### Relations` and `@relation(...)` (§3.2.2–§3.2.6) — under the section or written among the
fields — it reads the **names**: a `to` entry naming a key (`- >author` with `- from: author_id`, or
`@relation(author, -> Person, from: author_id)`) names that key's navigation, and a `from` entry on
the target naming a one-to-one's key (`- <profile` with `- target: Profile.owner_id`, or
`@relation(profile, <- Profile.owner_id)`) names its reverse navigation; an entry's `on_delete` is
read with the key's. Without a declared name a key's navigation is named after it
(`owner_id` → `Owner`); a key with no `_id` to strip, and a self-referencing one-to-one's reverse
side, get no navigation until one is declared (`MDD049`). An entry that names no key of the model,
or a different target than the key's, stops the build (`MDD050`).

Not generated, and listed as unconsumed: the many side of a many-to-one (`- <comments` naming an
ordinary key — a collection), many-to-many (`- <>tags`), and the hints `load`, `order_by`,
`on_update`. A field written in relationship notation is dropped before rendering, so it never
reaches a target as a column.

## §3.3.2 Model Level Indexes (Single Line)

**What this generator implements**: `name:` names the index or constraint in the generated SQL;
without it the name is built from the model and the columns. A name that is not an identifier, or
one another generated index already has, stops the build (`MDD042`). `fulltext: true` does not
produce a full-text index in either SQL dialect — the columns get an ordinary index, and the build
says so (`MDD041`).

## §3.3.3 Index Section (Recommended for Multiple Indexes)

**What this generator implements**: this one, in full — every entry of `### Indexes` is read. An
entry that names no columns makes no index, and the build says so (`MDD043`).

## §3.3.4.3 NULL Handling in Multi-Column Unique Constraints

**What this generator implements**: both meanings, on both SQL dialects and in the EF Core mapping.
With the default, a multi-column `@unique` over a nullable column becomes a filtered unique index on
SQL Server (`WHERE … IS NOT NULL`) and a plain unique constraint on PostgreSQL, whose default is
already that meaning. With `nulls: "not_distinct"` it is an unfiltered `UNIQUE` constraint on SQL
Server and `UNIQUE NULLS NOT DISTINCT` on PostgreSQL, which needs PostgreSQL 15 or later.

## §3.4.6 File Owner (# Prefix:)

**What this generator implements**: implemented. The prefix is read as declared, and the
naming policy the language leaves open is enforced here as a warning — MDD017, a model
defined in a prefixed file whose name does not start with that prefix (in PascalCase).

## §3.4.7 Extending a Model (::extend)

**What this generator implements**: implemented, on every target — SQL, Model, and TypeScript
each render an extension field as an ordinary field, with no target-specific handling of
`origin`/`extended_by`. The naming policy from §3.4.6 is enforced here as MDD013 (a field
extending another owner's model must carry the extender's prefix), MDD014 (a prefix-less file
may not extend a model a prefixed file owns), and MDD015 (a stored extension field must be
nullable or have a default — the target's existing rows have no value for a new required
column).

## §3.4.8 Models with a Base (::aspect, ::subtype)

**What this generator implements**: `::aspect` in full, `::subtype` not at all. An aspect is
stored as a table whose primary key is the base's key and a foreign key to it with
`ON DELETE CASCADE`; the key is created for you (declaring one is MDD021), and the base gains a
navigation named after the model with the base's name removed (MDD019 warns when the name does not
follow that convention, MDD020 stops the build when two names collide). A key that is itself a
reference to another model, in a model that declares no base, is MDD022 — the build asks you to
say `::aspect(Base)` rather than guessing what the shape means. `::subtype` fails the build with
MDD016: how a kind of Base is stored has not been decided.

## §3.5.2 Metadata Section (Recommended for Multiple Metadata)

**What this generator implements**: nothing. A `### Metadata` section is read by no target and
appears in no output; the build reports it as unconsumed.

## §4.1 Composite Key Definition

**What this generator implements**: the attribute form only. `@primary(1)`/`@primary(2)` is read as
a composite key. The multi-line `### PrimaryKey` form is not — the section is carried into the model
as unstructured entries and nothing reads them, so the fields it names are **not** marked as keys.
The build reports the section as unconsumed. Declare composite keys with `@primary(order)`.

## §4.3.2 Behavior Section (Recommended for Multiple Behaviors)

**What this generator implements**: nothing. Neither the `### Behaviors` section nor `@behavior` is
read by any target; the section is reported as unconsumed, but the bare `@behavior` attribute form
is not — nothing surfaces if a target starts reading it. Behaviour belongs to the application
running on the generated model, not to the generated model.

## §4.4.5 Platform-Specific Expressions

**What this generator implements** — the portable subset (§10.7) on both SQL dialects, every
expression context (model views, rollup filters, standalone views). A portable expression is
translated: `true`/`false` → `1`/`0` on SQL Server and `TRUE`/`FALSE` on PostgreSQL, `concat` →
`CONCAT`/`concat`, `date_add(d, n, 'day')` → `DATEADD(DAY, n, d)` / `(d + (n) * INTERVAL '1 day')`,
`length(s)` counts trailing spaces on SQL Server too (and is null for null, as on PostgreSQL), a
string literal is `N'…'` on SQL Server, and a boolean field's value is a `bit` there (a comparison
becomes a `CASE`; a boolean field standing alone as a condition is compared with 1). A computed value
of a numeric, date/time or identifier field is cast to the type the field declares, since the same
expression yields different types per dialect; text is not cast. An expression declared for
`sqlserver` or `postgresql` is emitted as written in that dialect. Missing an expression for the
dialect stops the build (`MDD036`), and so does an expression outside the subset in a portable
context (`MDD037`) — on either dialect, with every such expression listed at once. An expression that
is not well formed is outside the subset too: an unclosed string, a malformed number, a missing
parenthesis, an empty argument, two operands with no operator between them (a platform word such as
`ilike` written where an operator belongs), and `+` beside text — a literal, a function that returns
text, or a field the model declares as a string. So is a name the context cannot read: a model's
computed field reads its model's fields and the computed fields declared before it; a rollup's filter
reads the rolled-up model's fields (bare or qualified by its name) and `$parent.field`; a view's
filter reads its relations, a bare name being its `from`'s; a view's computed column reads the view's
own columns, `Relation.field` being the column that projects it. Platform expressions for `mysql` or
`sqlite` are carried and emitted by no target. The generated entity's `[Computed]` metadata is the
dialect's platform expression, else the portable one as written.

## §4.8.2 Complex Conditional Fields

**What this generator implements**: nothing. Both forms reach the model as plain attributes —
`@if(...)` as an attribute named `if`, the sub-field form as attributes named `required` and
`visible` — and no target reads any of them. A conditional field is emitted as an ordinary field,
silently.

## §4.9.3 Map Types

**What this generator implements**: none of these shapes. A field typed `object`, `map<K,V>`, an
inline `enum`, or an array `T[]` (§2.4.3) stops the build with `MDD048` — every target would have to
invent a storage for it, and an array used to reach them all as its element type without a word.
Model the parts as a separate model with a reference, a top-level `::enum`, or a serialized `text`
field.

## §4.10.3 Cross-Field Validation

**What this generator implements**: nothing. `@validate(...)` reaches the model as an attribute
and no target reads it — no validation is emitted from it, and none is checked at build time. Length
bounds in the output come from the declared type, not from here. A cross-field `### Validations`
section is carried and ignored too, though that one at least gets reported as unconsumed —
`@validate` itself does not, because an attribute is not a section.

## §4.11.2 Using Generic Types

**What this generator implements**: nothing. `## List<T>` is read as a model whose *name* is
the literal text `List<T>`, and `## ProductList : List<Product>` as a model inheriting from a model
named `List<Product>` — the type parameter is never substituted and no instantiation happens. The
output is whatever those literal names produce, which is not what this section describes.

## §5.2 External Schema References

**What this generator implements**: nothing — and it fails loudly rather than silently. A reference
target that names a scheme is not a model name this build loaded, so the build stops with `MDD012`,
which names the scheme and says the generator does not read one. (It does not report a missing
entity: the target was never meant to be an entity name, and saying so would send you looking for a
typo.)

To reference a table this build does not own, declare the column **without** `@reference`:

```markdown
- asset_id: identifier            # owned by another build; no @reference
```

The column is emitted with the type you gave it, and no foreign key, navigation property, or lookup
redirect is generated for it. That is a plain column, not a checked reference — nothing verifies
that the target exists or that the two sides still agree on its type.

## §6.1 Schema Versioning

**What this generator implements**: nothing. A `### Version` section is carried into the model as
unstructured entries that no target reads, so schema version information appears in no output. The
build reports the section as unconsumed.

## §6.2 Migration Notation

**What this generator implements**: nothing, by design. A `### Migration` section is carried and
ignored like the one above, and reported as unconsumed. This generator emits desired-state schema —
what the database should look like — and leaves reconciling an existing database with it to
whichever tool applies the output.

## §10.4.1 Primitive Types — row versions

**What this generator implements** for `rowversion` — the mapping differs by dialect, and every
target follows it:

| | SQL Server | PostgreSQL |
|---|---|---|
| Table | `[RowVersion] ROWVERSION NOT NULL` | no column — the `xmin` system column is the version |
| Views | projected like any column | `b.xmin AS row_version` where the view reads the table; the name where it reads another view |
| Entities (write and read) | `[Timestamp] public byte[] RowVersion` | `[Timestamp] public uint RowVersion`, mapped to `xmin` on the table |
| TypeScript | `RowVersion: string \| number` — the wire shape is the server's; compare, never compute | same |
| Field schema / forms | `readOnly: true`, not `required`; no form control | same |

`[Timestamp]` on the write type makes an update with a stale version affect no row; on the read
type it is what an OData model builder turns into the entity's ETag. An index on a row version is
generated on SQL Server and refused on PostgreSQL, where a system column cannot be indexed.
