// The consumer half of the gate for array fields.
//
// An array field is a TypeScript array on the entity, its element nullability inside the
// brackets and its own outside. The generated form draws no control for one, so it is not
// a key a consumer can override — the form state still carries it, typed as the array.

import type { ArrayHolder } from '../generated/types/entities_gen'
import { emptyArrayHolder, type ArrayHolderFormField } from '../generated/forms/ArrayHolderForm_gen'

declare const row: ArrayHolder

export const tags: string[] = row.Tags
export const notes: (string | null)[] = row.Notes
export const scores: number[] | null = row.Scores
export const ranks: (number | null)[] | null = row.Ranks

// @ts-expect-error a non-null element is not null
export const notNullable: string[] = row.Notes

// A caller who edits a list sets it on the form state itself.
export const withTags: Partial<ArrayHolder> = { ...emptyArrayHolder, Tags: ['a', 'b'] }

// @ts-expect-error an array field has no generated control, so it is no override key
export const notAnOverride: ArrayHolderFormField = 'Tags'
