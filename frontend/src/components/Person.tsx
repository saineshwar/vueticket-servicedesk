import Avatar from '@mui/material/Avatar'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import type { ReactNode } from 'react'

/** Initials avatar plus name. The colour comes from the name, so a person always looks the same across grids. */
export function Person({ name, detail }: { name: string; detail?: ReactNode }) {
  const initials = name.split(/\s+/).filter(Boolean).slice(0, 2).map((p) => p[0]!.toUpperCase()).join('')
  let hash = 0
  for (const c of name) hash = (hash * 31 + c.charCodeAt(0)) >>> 0
  // Golden-angle spacing keeps neighbouring names visibly different.
  const hue = Math.round((hash % 1000) * 137.508) % 360
  return (
    <Stack direction="row" spacing={1} sx={{ alignItems: 'center', minWidth: 0 }}>
      <Avatar aria-hidden sx={{ width: 26, height: 26, fontSize: '0.68rem', fontWeight: 700, bgcolor: `hsl(${hue} 60% 92%)`, color: `hsl(${hue} 50% 30%)` }}>
        {initials}
      </Avatar>
      <Stack sx={{ minWidth: 0 }}>
        <Typography variant="body2" noWrap>{name}</Typography>
        {detail}
      </Stack>
    </Stack>
  )
}
