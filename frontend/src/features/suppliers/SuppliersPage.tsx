import { useState } from 'react'
import { Link } from 'react-router'
import { useCan } from '@/app/auth/auth-context'
import { Policies } from '@/app/auth/policies'
import { PageHeader } from '@/components/ui/PageHeader'
import { SupplierDetailAside } from './components/SupplierDetailAside'
import { SupplierFormDialog } from './components/SupplierFormDialog'
import { SupplierTable, type PreviewRole } from './components/SupplierTable'

/** Frame 18b: the global supplier catalog, under Directory's "Organizations | Suppliers" sub-nav. */
export function SuppliersPage() {
  const canManage = useCan(Policies.SuppliersManage)
  const [openId, setOpenId] = useState<string | null>(null)
  const [adding, setAdding] = useState(false)
  const [previewRole, setPreviewRole] = useState<PreviewRole>('Board')

  return (
    <div className="flex flex-col gap-5">
      <PageHeader title="Directory" description="Venues, caterers, printers, tech/AV, merch and transport suppliers HTN works with." />
      <nav aria-label="Directory views" className="flex gap-5 border-b border-gray-200 dark:border-white/10">
        <Link to="/directory" className="flex h-11 items-center text-sm font-medium text-gray-600 hover:text-gray-900 dark:text-gray-300 dark:hover:text-gray-50">
          Organizations
        </Link>
        <span className="flex h-11 items-center border-b-2 border-brand-600 text-sm font-medium text-brand-700 dark:border-brand-300 dark:text-brand-300">
          Suppliers
        </span>
      </nav>
      <SupplierTable onOpen={setOpenId} onAdd={() => setAdding(true)} canManage={canManage} previewRole={previewRole} onPreviewRoleChange={setPreviewRole} />
      <SupplierDetailAside supplierId={openId} onClose={() => setOpenId(null)} canManage={canManage} previewRole={previewRole} />
      {canManage && <SupplierFormDialog open={adding} onClose={() => setAdding(false)} />}
    </div>
  )
}
