import type { DirectoryOrgKind } from '../schemas'

const kindLabels: Record<DirectoryOrgKind, string> = {
  Government: 'Government',
  Company: 'Company',
  University: 'University',
  NonProfit: 'Non-profit',
}

export function orgKindLabel(kind: DirectoryOrgKind): string {
  return kindLabels[kind]
}
