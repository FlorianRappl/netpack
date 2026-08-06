export interface Metafile {
  inputs: Record<string, InputFile>
  outputs: Record<string, OutputFile>
  audit?: AuditReport
}

export interface AuditReport {
  source?: string
  checked: number
  summary?: Record<string, number>
  vulnerabilities?: AuditVulnerability[]
  error?: string
}

export interface AuditVulnerability {
  name?: string
  version?: string
  severity?: string
  title?: string
  id?: string
  url?: string
  vulnerableVersions?: string
  cwe?: string[]
  cvssScore?: number
}

export interface InputFile {
  bytes: number
  imports: ImportRecord[]
  format?: 'cjs' | 'esm'
  with?: Record<string, string>
}

export interface OutputFile {
  bytes: number
  inputs: Record<string, InputForOutput>
  imports: ImportRecord[]
  exports: string[]
  entryPoint?: string
  cssBundle?: string
}

export interface ImportRecord {
  path: string
  kind: string
  external?: boolean
  original?: string
  with?: Record<string, string>
}

export interface InputForOutput {
  bytesInOutput: number
}
