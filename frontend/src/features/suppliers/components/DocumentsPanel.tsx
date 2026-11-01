import { useState } from 'react'
import { errorMessage } from '@/app/errors'
import { Button } from '@/components/ui/Button'
import { FileButton } from '@/components/ui/FileButton'
import { Select } from '@/components/ui/Input'
import { UploadIcon } from '@/components/ui/icons'
import { useToast } from '@/components/ui/toast-context'
import { formatDate } from '@/lib/format'
import { supplierDocumentDownloadUrl, useUploadSupplierDocumentMutation, useVoidSupplierDocumentMutation } from '../api'
import { supplierDocumentKinds, type SupplierDocument, type SupplierDocumentKind } from '../schemas'
import { documentKindLabel } from './labels'
import { ReasonDialog } from './ReasonDialog'

const DOC_MAX_BYTES = 5 * 1024 * 1024

function checkDoc(file: File): string | null {
  const okType = file.type === 'image/png' || file.type === 'image/jpeg' || file.type === 'application/pdf' || /\.(png|jpe?g|pdf)$/i.test(file.name)
  if (!okType) return 'Upload a PNG, JPG or PDF.'
  if (file.size > DOC_MAX_BYTES) return 'The document is larger than 5 MB.'
  return null
}

/** Frame 18b aside: BIR 2303 / business permit uploads, lead-visible (download), board-only to upload or void. */
export function DocumentsPanel({ supplierId, documents, canManage }: { supplierId: string; documents: SupplierDocument[]; canManage: boolean }) {
  const upload = useUploadSupplierDocumentMutation()
  const voidDoc = useVoidSupplierDocumentMutation()
  const { toast } = useToast()
  const [kind, setKind] = useState<SupplierDocumentKind>('Bir2303')
  const [uploadError, setUploadError] = useState<string | null>(null)
  const [voiding, setVoiding] = useState<SupplierDocument | null>(null)

  function onFile(file: File) {
    const problem = checkDoc(file)
    setUploadError(problem)
    if (problem) return
    upload.mutate(
      { id: supplierId, kind, file },
      {
        onSuccess: () => toast({ tone: 'success', title: 'Document uploaded.' }),
        onError: (e) => setUploadError(errorMessage(e, { 400: 'Upload a PNG, JPG or PDF up to 5 MB.' })),
      },
    )
  }

  const live = documents.filter((d) => !d.isVoided)

  return (
    <section className="flex flex-col gap-3">
      <h3 className="text-sm font-semibold text-gray-900 dark:text-gray-50">Documents</h3>
      {live.length === 0 ? (
        <p className="text-sm text-gray-500 dark:text-gray-400">No documents yet.</p>
      ) : (
        <ul className="flex flex-col gap-2">
          {live.map((d) => (
            <li key={d.id} className="flex items-center justify-between gap-2 rounded-md border border-gray-200 p-2.5 text-sm dark:border-white/10">
              <div className="flex flex-col">
                <span className="font-medium text-gray-900 dark:text-gray-50">{documentKindLabel(d.kind)}</span>
                <span className="text-xs text-gray-500 dark:text-gray-400">Uploaded {formatDate(d.uploadedAt)}</span>
              </div>
              <div className="flex items-center gap-2">
                <a
                  href={supplierDocumentDownloadUrl(supplierId, d.id)}
                  className="inline-flex min-h-9 items-center text-xs font-medium text-brand-600 hover:underline dark:text-brand-300"
                >
                  Download
                </a>
                {canManage ? (
                  <Button size="sm" variant="secondary" onClick={() => setVoiding(d)}>Remove</Button>
                ) : null}
              </div>
            </li>
          ))}
        </ul>
      )}

      {canManage ? (
        <div className="flex flex-wrap items-end gap-2">
          <label className="flex flex-col gap-1.5 text-sm">
            <span className="font-medium text-gray-700 dark:text-gray-200">Kind</span>
            <Select value={kind} onChange={(e) => setKind(e.target.value as SupplierDocumentKind)}>
              {supplierDocumentKinds.map((k) => (
                <option key={k} value={k}>{documentKindLabel(k)}</option>
              ))}
            </Select>
          </label>
          <FileButton accept="image/png,image/jpeg,.png,.jpg,.jpeg,application/pdf,.pdf" onFile={onFile} loading={upload.isPending} size="sm">
            <UploadIcon size={14} />
            Upload
          </FileButton>
        </div>
      ) : null}
      {uploadError ? <p role="alert" className="text-xs font-medium text-red-700 dark:text-red-400">{uploadError}</p> : null}

      <ReasonDialog
        open={voiding !== null}
        onClose={() => setVoiding(null)}
        title="Remove document"
        description="This keeps the old file and who uploaded it, instead of deleting the record."
        confirmLabel="Remove document"
        pending={voidDoc.isPending}
        onConfirm={(reason) => {
          if (!voiding) return
          voidDoc.mutate(
            { id: supplierId, docId: voiding.id, reason },
            {
              onSuccess: () => { toast({ tone: 'success', title: 'Document removed.' }); setVoiding(null) },
              onError: (e) => toast({ tone: 'error', title: 'Could not remove the document.', description: errorMessage(e) }),
            },
          )
        }}
      />
    </section>
  )
}

