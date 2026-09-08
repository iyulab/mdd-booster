// The `formControlsImport` contract, transcribed from README "소비 프로젝트 계약
// (TypeScript 타깃)". These are declarations, not an implementation: the contract this
// repository publishes is "what must be exported", never "what it must be built from".
//
// Every prop below is one the generator emits. Nothing here is aspirational — adding a
// prop the generator does not emit weakens the gate, and omitting one the generator does
// emit turns a real consumer break into a local failure, which is the point.
//
// The prop types are deliberately exact (`(v: string) => void`, not `Function`): the README
// warns that a wrapper passing the *event* instead of the value satisfies the name but not
// the contract, and only a typed signature can tell those apart.

import type { ReactNode } from 'react'

export interface UInputProps {
  label: string
  required?: boolean
  description?: string
  /** "date" | "datetime" | "number" — absent for plain strings. */
  type?: string
  step?: number
  maxlength?: number
  disabled?: boolean
  error?: string
  value: string
  onChange: (v: string) => void
}

export interface UTextareaProps {
  label: string
  required?: boolean
  description?: string
  minRows: number
  disabled?: boolean
  error?: string
  value: string
  onChange: (v: string) => void
}

export interface USelectProps {
  label: string
  required?: boolean
  description?: string
  placeholder?: string
  disabled?: boolean
  error?: string
  value: string
  /** Whatever `enumToOptions` returns — the consumer owns this type. */
  options: unknown
  onChange: (v: string) => void
}

export interface UCheckboxProps {
  label: string
  description?: string
  disabled?: boolean
  error?: string
  checked: boolean
  onChange: (v: boolean) => void
}

export declare function UInput(props: UInputProps): ReactNode
export declare function UTextarea(props: UTextareaProps): ReactNode
export declare function USelect(props: USelectProps): ReactNode
export declare function UCheckbox(props: UCheckboxProps): ReactNode
