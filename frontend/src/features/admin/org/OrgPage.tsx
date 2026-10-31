import ApartmentOutlined from '@mui/icons-material/ApartmentOutlined'
import BusinessOutlined from '@mui/icons-material/BusinessOutlined'
import ExpandMore from '@mui/icons-material/ExpandMore'
import ChevronRight from '@mui/icons-material/ChevronRight'
import GroupsOutlined from '@mui/icons-material/GroupsOutlined'
import WorkOutlineOutlined from '@mui/icons-material/WorkOutlineOutlined'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import Chip from '@mui/material/Chip'
import CircularProgress from '@mui/material/CircularProgress'
import Divider from '@mui/material/Divider'
import IconButton from '@mui/material/IconButton'
import List from '@mui/material/List'
import ListItem from '@mui/material/ListItem'
import ListItemText from '@mui/material/ListItemText'
import Paper from '@mui/material/Paper'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useMemo, useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { orgApi, roleApi } from '../../../api/endpoints'
import type { OrgUnit, OrgUnitType } from '../../../api/types'
import { useAuth } from '../../../auth/authContext'
import { ErrorAlert } from '../../../components/ErrorAlert'
import { GiveRoleDialog } from './GiveRoleDialog'
import { buildTree, childTypesFor, type TreeNode } from './orgTree'
import { AddUnitDialog, EditUnitDialog, MoveUnitDialog } from './UnitDialogs'

const typeIcon: Record<OrgUnitType, ReactNode> = {
  Organization: <BusinessOutlined fontSize="small" />,
  BusinessUnit: <ApartmentOutlined fontSize="small" />,
  Department: <WorkOutlineOutlined fontSize="small" />,
  Team: <GroupsOutlined fontSize="small" />,
}

type DialogState = { kind: 'add' | 'edit' | 'move' | 'role'; unit: OrgUnit } | null

export function OrgPage() {
  const { t } = useTranslation()
  const { can } = useAuth()
  const units = useQuery({ queryKey: ['org-units'], queryFn: orgApi.tree })
  const [selectedId, setSelectedId] = useState<number | null>(null)
  const [collapsed, setCollapsed] = useState<Set<number>>(new Set())
  const [dialog, setDialog] = useState<DialogState>(null)

  const tree = useMemo(() => buildTree(units.data ?? []), [units.data])
  const selected = units.data?.find((u) => u.orgUnitId === selectedId) ?? null

  const toggle = (id: number) =>
    setCollapsed((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })

  const renderNode = (node: TreeNode): ReactNode => {
    const isOpen = !collapsed.has(node.orgUnitId)
    const isSelected = node.orgUnitId === selectedId
    return (
      <Box component="li" key={node.orgUnitId} role="treeitem" aria-expanded={node.children.length ? isOpen : undefined} aria-selected={isSelected} sx={{ listStyle: 'none' }}>
        <Stack
          direction="row"
          sx={{
            alignItems: 'center',
            gap: 0.5,
            pl: node.depth * 2.5,
            pr: 1,
            py: 0.25,
            borderRadius: 1,
            cursor: 'pointer',
            bgcolor: isSelected ? 'action.selected' : 'transparent',
            '&:hover': { bgcolor: isSelected ? 'action.selected' : 'action.hover' },
          }}
          onClick={() => setSelectedId(node.orgUnitId)}
        >
          <IconButton
            size="small"
            aria-label={isOpen ? 'Collapse' : 'Expand'}
            sx={{ visibility: node.children.length ? 'visible' : 'hidden' }}
            onClick={(e) => { e.stopPropagation(); toggle(node.orgUnitId) }}
          >
            {isOpen ? <ExpandMore fontSize="small" /> : <ChevronRight fontSize="small" />}
          </IconButton>
          <Box sx={{ color: 'text.secondary', display: 'flex' }}>{typeIcon[node.unitType]}</Box>
          <Typography variant="body2" sx={{ fontWeight: isSelected ? 650 : 500, flexGrow: 1, minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
            {node.name}
          </Typography>
          {!node.isActive && <Chip size="small" label={t('org.inactive')} />}
          <Typography variant="caption" sx={{ color: 'text.secondary', fontFamily: 'Consolas, ui-monospace, monospace' }}>
            {node.code}
          </Typography>
        </Stack>
        {isOpen && node.children.length > 0 && (
          <Box component="ul" role="group" sx={{ m: 0, p: 0 }}>
            {node.children.map(renderNode)}
          </Box>
        )}
      </Box>
    )
  }

  return (
    <Stack spacing={2}>
      <div>
        <Typography variant="h1" component="h1">{t('org.title')}</Typography>
        <Typography sx={{ color: 'text.secondary', mt: 0.5 }}>{t('org.intro')}</Typography>
      </div>
      <ErrorAlert error={units.error} />
      <Stack direction={{ xs: 'column', md: 'row' }} spacing={2} sx={{ alignItems: 'flex-start' }}>
        <Paper variant="outlined" sx={{ p: 1, width: { xs: '100%', md: 420 }, flexShrink: 0, minWidth: 0 }}>
          {units.isLoading ? (
            <Box sx={{ p: 3, textAlign: 'center' }}><CircularProgress size={24} /></Box>
          ) : (
            <Box component="ul" role="tree" aria-label={t('org.title')} sx={{ m: 0, p: 0 }}>
              {tree.map(renderNode)}
            </Box>
          )}
        </Paper>

        <Paper variant="outlined" sx={{ p: 2.5, flexGrow: 1, width: '100%', minWidth: 0 }}>
          {selected ? (
            <UnitDetails
              unit={selected}
              canManageOrg={can('org.manage')}
              canManageRoles={can('role.manage')}
              onAction={(kind) => setDialog({ kind, unit: selected })}
            />
          ) : (
            <Typography sx={{ color: 'text.secondary' }}>{t('org.selectUnit')}</Typography>
          )}
        </Paper>
      </Stack>

      {dialog?.kind === 'add' && <AddUnitDialog parent={dialog.unit} onClose={(id) => { setDialog(null); if (id) setSelectedId(id) }} />}
      {dialog?.kind === 'edit' && <EditUnitDialog unit={dialog.unit} onClose={() => setDialog(null)} />}
      {dialog?.kind === 'move' && <MoveUnitDialog unit={dialog.unit} units={units.data ?? []} onClose={() => setDialog(null)} />}
      {dialog?.kind === 'role' && <GiveRoleDialog unit={dialog.unit} onClose={() => setDialog(null)} />}
    </Stack>
  )
}

function UnitDetails({
  unit,
  canManageOrg,
  canManageRoles,
  onAction,
}: {
  unit: OrgUnit
  canManageOrg: boolean
  canManageRoles: boolean
  onAction: (kind: 'add' | 'edit' | 'move' | 'role') => void
}) {
  const { t } = useTranslation()
  const qc = useQueryClient()
  const assignments = useQuery({
    queryKey: ['assignments', unit.orgUnitId],
    queryFn: () => roleApi.assignments({ orgUnitId: unit.orgUnitId }),
    enabled: canManageRoles,
  })
  const remove = useMutation({
    mutationFn: roleApi.unassign,
    onSuccess: () => qc.invalidateQueries({ queryKey: ['assignments'] }),
  })

  return (
    <Stack spacing={2}>
      <Stack direction="row" spacing={1} sx={{ alignItems: 'flex-start', flexWrap: 'wrap', rowGap: 1 }}>
        <Box sx={{ flexGrow: 1, minWidth: 0 }}>
          <Typography variant="h2" component="h2">{unit.name}</Typography>
          <Typography variant="body2" sx={{ color: 'text.secondary' }}>
            {t(`unitType.${unit.unitType}`)} · <span style={{ fontFamily: 'Consolas, ui-monospace, monospace' }}>{unit.code}</span> ·{' '}
            {t('org.members', { count: unit.memberCount })}
          </Typography>
        </Box>
        {canManageOrg && (
          <Stack direction="row" spacing={1}>
            {childTypesFor(unit.unitType).length > 0 && (
              <Button variant="contained" onClick={() => onAction('add')}>{t('org.addChild')}</Button>
            )}
            <Button variant="outlined" onClick={() => onAction('edit')}>{t('org.edit')}</Button>
            {unit.parentId != null && <Button variant="outlined" onClick={() => onAction('move')}>{t('org.move')}</Button>}
          </Stack>
        )}
      </Stack>

      {canManageRoles && (
        <>
          <Divider />
          <Stack direction="row" sx={{ alignItems: 'center' }}>
            <Typography variant="h3" component="h3" sx={{ flexGrow: 1 }}>{t('org.roleAssignments')}</Typography>
            <Button onClick={() => onAction('role')}>{t('org.giveRole')}</Button>
          </Stack>
          <ErrorAlert error={assignments.error ?? remove.error} />
          {assignments.data && assignments.data.length === 0 && (
            <Typography variant="body2" sx={{ color: 'text.secondary' }}>{t('org.noAssignments')}</Typography>
          )}
          <List dense disablePadding>
            {assignments.data?.map((a) => (
              <ListItem
                key={a.roleAssignmentId}
                divider
                secondaryAction={
                  <Button size="small" color="error" disabled={remove.isPending} onClick={() => remove.mutate(a.roleAssignmentId)}>
                    {t('org.remove')}
                  </Button>
                }
              >
                <ListItemText
                  primary={`${a.userDisplayName} · ${a.roleName}`}
                  secondary={a.includeDescendants ? t('org.includesSubUnits') : t('org.thisUnitOnly')}
                />
              </ListItem>
            ))}
          </List>
        </>
      )}
    </Stack>
  )
}
