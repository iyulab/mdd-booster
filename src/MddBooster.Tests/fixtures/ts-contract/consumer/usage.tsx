// The consumer half of the type gate.
//
// The generated files next door only have to type-check *against each other* to compile.
// That is not the same as being usable: `fieldOverrides`, `slots`, `sectionProps` and
// `errors` exist so a consumer can pass something in, and nothing in the generated output
// exercises the types they publish. This file does, so a change that keeps the generator
// self-consistent while making its surface unusable fails here.
//
// The negative assertions carry as much as the positive ones. Rejecting a slot field, a
// derived field, or a typo is the *designed* behaviour of the override key type — a
// `keyof Entity` key would accept all three and quietly do nothing, which is the failure
// that type was chosen to prevent. `@ts-expect-error` states that inversion directly:
// tsc fails if the marked line stops being an error, so widening the key type turns this
// file red rather than silently removing the guarantee.

import type { ReactNode } from 'react'
import type { Everything } from '../generated/types/entities_gen'
import {
  EverythingFormBase,
  type EverythingFieldOverrides,
  type EverythingFormField,
  type EverythingFormSectionProps,
  type EverythingFormSlots,
} from '../generated/forms/EverythingForm_gen'

// --- every prop the form publishes, actually supplied -------------------------------

const slots: EverythingFormSlots = {
  OwnerId: null as ReactNode,
}

const sectionProps: EverythingFormSectionProps = {
  기본: { className: 'cols-2', style: { gap: 8 } },
}

// The ctx is per-field: `value`/`onChange` follow that field's own type, which is the
// whole point of the mapped type. Reading them here is what pins it — an override typed
// as a single shared shape would still compile if these were ignored.
const fieldOverrides: EverythingFieldOverrides = {
  Title: ({ value, onChange, label, required, error }) => {
    const text: string | undefined = value
    return `${label}${required ? '*' : ''}${text ?? ''}${error ?? ''}${onChange.length}` as unknown as ReactNode
  },
  Done: ({ value, onChange }) => {
    const flag: boolean | undefined = value
    onChange(!flag)
    return null
  },
  Qty: ({ value, onChange }) => {
    const n: number | undefined = value
    onChange((n ?? 0) + 1)
    return null
  },
}

const errors: Partial<Record<keyof Everything, string>> = { Title: '필수입니다' }

export function EverythingScreen(form: Partial<Everything>) {
  return (
    <EverythingFormBase
      form={form}
      onChange={() => {}}
      slots={slots}
      fieldOverrides={fieldOverrides}
      sectionProps={sectionProps}
      errors={errors}
    />
  )
}

// --- the keys the override type must refuse -----------------------------------------

// A slot field is already the consumer's, and two ways to own one field would make
// "which wins" a new contract.
// @ts-expect-error OwnerId renders through `slots`, never through `fieldOverrides`
export const slotKeyIsRejected: EverythingFieldOverrides = { OwnerId: () => null }

// @ts-expect-error a misspelt field must not become a prop that silently does nothing
export const typoKeyIsRejected: EverythingFieldOverrides = { Titel: () => null }

// The field union is narrower than `keyof Everything`: entity members the form does not
// render a control for are not override targets either.
// @ts-expect-error `Id` is on the entity but the form draws no control for it
export const nonFieldKeyIsRejected: EverythingFieldOverrides = { Id: () => null }

// And the union itself is what those three answer to.
export const fieldUnionIsNarrowerThanTheEntity: EverythingFormField = 'Title'
// @ts-expect-error same inversion one level down — the union must not admit a slot field
export const slotIsNotInTheFieldUnion: EverythingFormField = 'OwnerId'
