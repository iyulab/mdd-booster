// The consumer half of the gate for a view registered as a read-only set.
//
// A view has a read type and a set name, and no write type. Its name is published apart
// from the writable sets so that `EntitySetName` — the type an edit form or a permission
// map takes — does not widen to admit a set nothing can be written to. A list screen
// reads either kind and spells that out as the union.

import type { ActiveProducts } from '../generated/types/entities_gen'
import { READ_ONLY_SET_NAMES, type EntitySetName, type ReadOnlySetName } from '../generated/types/entity_names_gen'

export const readableByAList: EntitySetName | ReadOnlySetName = 'ActiveProducts'
export const everyReadOnlySet: readonly ReadOnlySetName[] = READ_ONLY_SET_NAMES

// @ts-expect-error a read-only set is not one an edit form or a permission map writes to
export const notAWritableSet: EntitySetName = 'ActiveProducts'

// The read type is the row a list renders — every column the view selects, as read.
export const row: ActiveProducts = { Id: '', Name: '', Price: 0, Stock: 0 }
