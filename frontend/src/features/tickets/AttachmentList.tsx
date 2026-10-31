import InsertDriveFileOutlined from '@mui/icons-material/InsertDriveFileOutlined'
import LockOutlined from '@mui/icons-material/LockOutlined'
import Chip from '@mui/material/Chip'
import Stack from '@mui/material/Stack'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ticketApi } from '../../api/endpoints'
import type { Attachment } from '../../api/types'
import { ErrorAlert } from '../../components/ErrorAlert'
import { formatSize } from '../../components/fileFormat'

export function AttachmentList({ attachments }: { attachments: Attachment[] }) {
  const { t } = useTranslation()
  const [error, setError] = useState<unknown>(null)
  if (attachments.length === 0) return null

  const open = async (a: Attachment) => {
    setError(null)
    try {
      await ticketApi.download(a.attachmentId, a.fileName)
    } catch (err) {
      setError(err)
    }
  }

  return (
    <Stack spacing={1} sx={{ mt: 1.5 }}>
      <Stack direction="row" sx={{ flexWrap: 'wrap', gap: 1 }}>
        {attachments.map((a) => (
          <Chip
            key={a.attachmentId}
            size="small"
            variant="outlined"
            clickable
            icon={a.isInternal ? <LockOutlined /> : <InsertDriveFileOutlined />}
            label={`${a.fileName} · ${formatSize(a.sizeBytes)}`}
            onClick={() => void open(a)}
            aria-label={t('files.download', { name: a.fileName })}
          />
        ))}
      </Stack>
      <ErrorAlert error={error} />
    </Stack>
  )
}
