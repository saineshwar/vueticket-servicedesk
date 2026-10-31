import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import Stack from '@mui/material/Stack'
import Table from '@mui/material/Table'
import TableBody from '@mui/material/TableBody'
import TableCell from '@mui/material/TableCell'
import TableHead from '@mui/material/TableHead'
import TableRow from '@mui/material/TableRow'
import Typography from '@mui/material/Typography'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { DailyFlow } from '../../api/types'

// Categorical slots 1 (blue) and 3 (aqua) of the validated chart palette, stepped per mode.
// Light: both pass every check; aqua is below 3:1 on white, so values are always reachable by tooltip and table.
const seriesVars = {
  '--series-created': '#2a78d6',
  '--series-resolved': '#1baf7a',
}
const seriesVarsDark = {
  '--series-created': '#3987e5',
  '--series-resolved': '#199e70',
}

const width = 640
const height = 200
const pad = { top: 12, right: 8, bottom: 26, left: 32 }
const barWidth = 10
const barGap = 2

const dayLabel = new Intl.DateTimeFormat(undefined, { day: 'numeric', month: 'short' })
const parseDay = (d: string) => new Date(`${d}T00:00:00Z`)

/** Top-rounded bar anchored to the baseline (4px data-end radius, square at the axis). */
function barPath(x: number, y: number, w: number, h: number) {
  if (h <= 0) return ''
  const r = Math.min(4, w / 2, h)
  return `M${x},${y + h}V${y + r}Q${x},${y} ${x + r},${y}H${x + w - r}Q${x + w},${y} ${x + w},${y + r}V${y + h}Z`
}

/** Tickets created vs resolved per day, last 14 days. Grouped bars, one y-axis, hover per day, table view. */
export function FlowChart({ days }: { days: DailyFlow[] }) {
  const { t } = useTranslation()
  const [hover, setHover] = useState<number | null>(null)
  const [asTable, setAsTable] = useState(false)

  const max = Math.max(1, ...days.flatMap((d) => [d.created, d.resolved]))
  // A "nice" top: 1, 2, 5 × 10ⁿ so gridlines land on round numbers.
  const magnitude = 10 ** Math.floor(Math.log10(max))
  const top = [1, 2, 5, 10].map((m) => m * magnitude).find((v) => v >= max) ?? max
  const ticks = [0, top / 2, top]

  const plotW = width - pad.left - pad.right
  const plotH = height - pad.top - pad.bottom
  const slot = plotW / days.length
  const y = (v: number) => pad.top + plotH - (v / top) * plotH
  const groupX = (i: number) => pad.left + i * slot + (slot - (barWidth * 2 + barGap)) / 2

  const series = [
    { key: 'created' as const, label: t('dashboard.created'), color: 'var(--series-created)' },
    { key: 'resolved' as const, label: t('dashboard.resolved'), color: 'var(--series-resolved)' },
  ]
  const totals = { created: days.reduce((s, d) => s + d.created, 0), resolved: days.reduce((s, d) => s + d.resolved, 0) }
  const hovered = hover === null ? null : days[hover]

  return (
    <Box sx={(theme) => ({ ...seriesVars, ...theme.applyStyles('dark', seriesVarsDark) })}>
      <Stack direction="row" sx={{ alignItems: 'center', gap: 2, flexWrap: 'wrap', mb: 1 }}>
        {/* Legend: identity is never colour alone; each entry also carries its 14-day total. */}
        {series.map((s) => (
          <Stack key={s.key} direction="row" spacing={0.75} sx={{ alignItems: 'center' }}>
            <Box sx={{ width: 10, height: 10, borderRadius: '3px', bgcolor: s.color }} />
            <Typography variant="body2">{s.label}</Typography>
            <Typography variant="body2" sx={{ color: 'text.secondary', fontVariantNumeric: 'tabular-nums' }}>{totals[s.key]}</Typography>
          </Stack>
        ))}
        <Box sx={{ flexGrow: 1 }} />
        <Button size="small" onClick={() => setAsTable((v) => !v)}>{asTable ? t('dashboard.showChart') : t('dashboard.showTable')}</Button>
      </Stack>

      {asTable ? (
        <Box sx={{ overflowX: 'auto', maxHeight: 260 }}>
          <Table size="small" aria-label={t('dashboard.flowTitle')}>
            <TableHead>
              <TableRow>
                <TableCell>{t('dashboard.day')}</TableCell>
                <TableCell align="right">{t('dashboard.created')}</TableCell>
                <TableCell align="right">{t('dashboard.resolved')}</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {days.map((d) => (
                <TableRow key={d.date}>
                  <TableCell>{dayLabel.format(parseDay(d.date))}</TableCell>
                  <TableCell align="right" sx={{ fontVariantNumeric: 'tabular-nums' }}>{d.created}</TableCell>
                  <TableCell align="right" sx={{ fontVariantNumeric: 'tabular-nums' }}>{d.resolved}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </Box>
      ) : (
        <Box sx={{ position: 'relative' }}>
          <svg viewBox={`0 0 ${width} ${height}`} width="100%" role="img" aria-label={t('dashboard.flowSummary', totals)}
            style={{ display: 'block', overflow: 'visible' }} onMouseLeave={() => setHover(null)}>
            {ticks.map((v) => (
              <g key={v}>
                <line x1={pad.left} x2={width - pad.right} y1={y(v)} y2={y(v)} stroke="var(--mui-palette-divider)" strokeWidth={1} />
                <text x={pad.left - 8} y={y(v)} dy="0.32em" textAnchor="end" fontSize={11} fill="var(--mui-palette-text-secondary)">{v}</text>
              </g>
            ))}
            {days.map((d, i) => (
              <g key={d.date}>
                {hover === i && <rect x={pad.left + i * slot} y={pad.top} width={slot} height={plotH} fill="var(--mui-palette-action-hover)" />}
                <path d={barPath(groupX(i), y(d.created), barWidth, y(0) - y(d.created))} fill="var(--series-created)" />
                <path d={barPath(groupX(i) + barWidth + barGap, y(d.resolved), barWidth, y(0) - y(d.resolved))} fill="var(--series-resolved)" />
                {/* Labels on every other day keep the axis readable at 14 days. */}
                {(days.length - 1 - i) % 2 === 0 && (
                  <text x={pad.left + i * slot + slot / 2} y={height - 8} textAnchor="middle" fontSize={11} fill="var(--mui-palette-text-secondary)">
                    {dayLabel.format(parseDay(d.date))}
                  </text>
                )}
                {/* Hit target: the whole day column, larger than the bars. */}
                <rect x={pad.left + i * slot} y={pad.top} width={slot} height={plotH} fill="transparent" onMouseEnter={() => setHover(i)} />
              </g>
            ))}
          </svg>
          {hovered && hover !== null && (
            <Box role="status" sx={{
              position: 'absolute', top: 0, pointerEvents: 'none',
              left: `${((pad.left + hover * slot + slot / 2) / width) * 100}%`,
              transform: hover > days.length / 2 ? 'translateX(calc(-100% - 12px))' : 'translateX(12px)',
              bgcolor: 'background.paper', border: 1, borderColor: 'divider', borderRadius: 1.5, px: 1.5, py: 1, boxShadow: 2, minWidth: 140,
            }}>
              <Typography variant="caption" sx={{ color: 'text.secondary' }}>{dayLabel.format(parseDay(hovered.date))}</Typography>
              {series.map((s) => (
                <Stack key={s.key} direction="row" spacing={1} sx={{ alignItems: 'center' }}>
                  <Box sx={{ width: 8, height: 8, borderRadius: '2px', bgcolor: s.color }} />
                  <Typography variant="body2" sx={{ flexGrow: 1 }}>{s.label}</Typography>
                  <Typography variant="body2" sx={{ fontWeight: 600, fontVariantNumeric: 'tabular-nums' }}>{hovered[s.key]}</Typography>
                </Stack>
              ))}
            </Box>
          )}
        </Box>
      )}
    </Box>
  )
}
