import Stack from '@mui/material/Stack'
import TextField from '@mui/material/TextField'
import Typography from '@mui/material/Typography'
import { useTranslation } from 'react-i18next'
import { splitMinutes } from './slaFormat'

/** Hours + minutes of business time, stored as total minutes. */
export function DurationField({ id, label, value, onChange, helperText }: {
  id: string
  label: string
  value: number
  onChange: (minutes: number) => void
  helperText?: string
}) {
  const { t } = useTranslation()
  const { hours, minutes } = splitMinutes(value)
  const set = (h: number, m: number) => onChange(Math.max(0, (Number.isFinite(h) ? h : 0) * 60 + (Number.isFinite(m) ? m : 0)))
  return (
    <Stack spacing={0.5} role="group" aria-labelledby={`${id}-label`}>
      <Typography id={`${id}-label`} variant="body2" sx={{ color: 'text.secondary' }}>{label}</Typography>
      <Stack direction="row" spacing={1}>
        <TextField id={`${id}-hours`} type="number" size="small" label={t('sla.hours')} value={hours}
          onChange={(e) => set(Number(e.target.value), minutes)} slotProps={{ htmlInput: { min: 0, max: 8760 } }} sx={{ width: 110 }} />
        <TextField id={`${id}-minutes`} type="number" size="small" label={t('sla.minutes')} value={minutes}
          onChange={(e) => set(hours, Number(e.target.value))} slotProps={{ htmlInput: { min: 0, max: 59 } }} sx={{ width: 110 }} />
      </Stack>
      {helperText && <Typography variant="caption" sx={{ color: 'text.secondary' }}>{helperText}</Typography>}
    </Stack>
  )
}
