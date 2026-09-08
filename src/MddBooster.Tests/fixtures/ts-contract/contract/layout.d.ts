// The `formLayoutImport` contract — README "formLayoutImport 가 가리킬 모듈".
//
// `className`/`style` are always emitted onto every <FormSection>, whether or not the
// consumer passes `sectionProps` — a FormSection that does not declare them breaks the
// compile of every generated form, including for consumers that never use the feature.
// That asymmetry is exactly what this declaration pins.

import type { CSSProperties, ReactNode } from 'react'

export interface FormSectionProps {
  title: string
  className?: string
  style?: CSSProperties
  children?: ReactNode
}

export interface FormRowProps {
  full?: boolean
  children?: ReactNode
}

export declare function FormSection(props: FormSectionProps): ReactNode
export declare function FormRow(props: FormRowProps): ReactNode
