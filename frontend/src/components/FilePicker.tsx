import AttachFileOutlined from '@mui/icons-material/AttachFileOutlined'
import Alert from '@mui/material/Alert'
import Button from '@mui/material/Button'
import Chip from '@mui/material/Chip'
import Stack from '@mui/material/Stack'
import { useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { formatSize } from './fileFormat'

// Mirrors AttachmentPolicy on the server, which stays the authority.
const allowed = ['.pdf', '.png', '.jpg', '.jpeg', '.gif', '.zip', '.docx', '.xlsx', '.pptx', '.msg', '.txt', '.log', '.csv', '.eml']
const maxBytes = 10 * 1024 * 1024
const maxFiles = 5

export function FilePicker({ files, onChange, disabled }: { files: File[]; onChange: (files: File[]) => void; disabled?: boolean }) {
  const { t } = useTranslation()
  const input = useRef<HTMLInputElement>(null)
  const [problem, setProblem] = useState<string | null>(null)

  const add = (picked: FileList | null) => {
    if (!picked) return
    const next = [...files]
    for (const file of Array.from(picked)) {
      const ext = file.name.slice(file.name.lastIndexOf('.')).toLowerCase()
      if (!allowed.includes(ext)) { setProblem(t('files.typeNotAllowed', { name: file.name })); continue }
      if (file.size > maxBytes) { setProblem(t('files.tooBig', { name: file.name })); continue }
      if (next.length >= maxFiles) { setProblem(t('files.tooMany', { max: maxFiles })); break }
      next.push(file)
      setProblem(null)
    }
    onChange(next)
    if (input.current) input.current.value = ''
  }

  return (
    <Stack spacing={1}>
      <Stack direction="row" sx={{ flexWrap: 'wrap', gap: 1, alignItems: 'center' }}>
        <Button size="small" startIcon={<AttachFileOutlined />} onClick={() => input.current?.click()} disabled={disabled || files.length >= maxFiles}>
          {t('files.attach')}
        </Button>
        <input ref={input} type="file" multiple hidden accept={allowed.join(',')} onChange={(e) => add(e.target.files)} aria-label={t('files.attach')} />
        {files.map((file, i) => (
          <Chip key={`${file.name}-${i}`} size="small" label={`${file.name} · ${formatSize(file.size)}`}
            onDelete={disabled ? undefined : () => onChange(files.filter((_, j) => j !== i))} />
        ))}
      </Stack>
      {problem && <Alert severity="warning" onClose={() => setProblem(null)}>{problem}</Alert>}
    </Stack>
  )
}
