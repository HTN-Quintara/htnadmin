import { useState } from 'react'
import { Link } from 'react-router'
import { useCan } from '@/app/auth/auth-context'
import { Policies } from '@/app/auth/policies'
import { PageHeader } from '@/components/ui/PageHeader'
import { OrgDetailAside } from './components/OrgDetailAside'
import { OrgFormDialog } from './components/OrgFormDialog'
import { OrgTable } from './components/OrgTable'

/**
 * Frame 18: the global directory of organizations (government, companies, universities, non-profits). The
 * "Organizations | Suppliers" sub-nav mirrors the Agenda page's "Programme | Speakers" tabs (frame 16): an underline
 * nav with a plain `<span>` for the current tab and a `<Link>` to the sibling. Unlike Agenda/Speakers — where only
 * the forward link exists — this one is symmetric (each page links to the other), since both halves of the
 * Directory are landable on their own and a one-way link would leave Suppliers with no way back.
 */
export function DirectoryPage() {
  const canManage = useCan(Policies.DirectoryManage)
  const canSuppliers = useCan(Policies.SuppliersView)
  const [openId, setOpenId] = useState<string | null>(null)
  const [adding, setAdding] = useState(false)

  return (
    <div className="flex flex-col gap-5">
      <PageHeader title="Directory" description="Agencies, companies, universities and non-profits HTN works with, and who to write to." />
      <nav aria-label="Directory views" className="flex gap-5 border-b border-gray-200 dark:border-white/10">
        <span className="flex h-11 items-center border-b-2 border-brand-600 text-sm font-medium text-brand-700 dark:border-brand-300 dark:text-brand-300">
          Organizations
        </span>
        {canSuppliers ? (
          <Link to="suppliers" className="flex h-11 items-center text-sm font-medium text-gray-600 hover:text-gray-900 dark:text-gray-300 dark:hover:text-gray-50">
            Suppliers
          </Link>
        ) : null}
      </nav>
      <OrgTable onOpen={setOpenId} onAdd={() => setAdding(true)} canManage={canManage} />
      <OrgDetailAside orgId={openId} onClose={() => setOpenId(null)} />
      {canManage && <OrgFormDialog open={adding} onClose={() => setAdding(false)} />}
    </div>
  )
}
