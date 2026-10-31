import Box from '@mui/material/Box'
import Paper from '@mui/material/Paper'
import Typography from '@mui/material/Typography'
import type { ReactNode } from 'react'

export function AuthCard({ title, intro, children, width = 420 }: { title: string; intro?: string; children: ReactNode; width?: number }) {
  return (
    <Box sx={{ minHeight: '100vh', display: 'grid', placeItems: 'center', bgcolor: 'background.default', px: 2, py: 4 }}>
      <Paper variant="outlined" sx={{ width: '100%', maxWidth: width, p: { xs: 3, sm: 4 } }}>
        <Typography sx={{ fontWeight: 700, color: 'primary.main', mb: 2 }}>VueTicket</Typography>
        <Typography variant="h1" component="h1" sx={{ mb: intro ? 1 : 3 }}>
          {title}
        </Typography>
        {intro && (
          <Typography sx={{ color: 'text.secondary', mb: 3 }}>{intro}</Typography>
        )}
        {children}
      </Paper>
    </Box>
  )
}
