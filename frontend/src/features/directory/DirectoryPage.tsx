import { useState } from 'react'
import { useCan } from '@/app/auth/auth-context'
import { Policies } from '@/app/auth/policies'
import { PageHeader } from '@/components/ui/PageHeader'
import { OrgDetailAside } from './components/OrgDetailAside'
import { OrgFormDialog } from './components/OrgFormDialog'
import { OrgTable } from './components/OrgTable'

/** Frame 18: the global directory of organizations (government, companies, universities, non-profits). */
export function DirectoryPage() {
  const canManage = useCan(Policies.DirectoryManage)
  const [openId, setOpenId] = useState<string | null>(null)
  const [adding, setAdding] = useState(false)

  return (
    <div className="flex flex-col gap-5">
      <PageHeader title="Directory" description="Agencies, companies, universities and non-profits HTN works with, and who to write to." />
      <OrgTable onOpen={setOpenId} onAdd={() => setAdding(true)} canManage={canManage} />
      <OrgDetailAside orgId={openId} onClose={() => setOpenId(null)} />
      {canManage && <OrgFormDialog open={adding} onClose={() => setAdding(false)} />}
    </div>
  )
}
