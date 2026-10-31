import type { OrgUnit, OrgUnitType } from '../../../api/types'

export const unitTypeOrder: OrgUnitType[] = ['Organization', 'BusinessUnit', 'Department', 'Team']

export interface TreeNode extends OrgUnit {
  children: TreeNode[]
}

/** Builds the nested tree from the flat API list, children sorted by type then name. */
export function buildTree(units: OrgUnit[]): TreeNode[] {
  const byId = new Map<number, TreeNode>()
  units.forEach((u) => byId.set(u.orgUnitId, { ...u, children: [] }))
  const roots: TreeNode[] = []
  byId.forEach((node) => {
    const parent = node.parentId != null ? byId.get(node.parentId) : undefined
    if (parent) parent.children.push(node)
    else roots.push(node)
  })
  const sort = (nodes: TreeNode[]) => {
    nodes.sort(
      (a, b) => unitTypeOrder.indexOf(a.unitType) - unitTypeOrder.indexOf(b.unitType) || a.name.localeCompare(b.name),
    )
    nodes.forEach((n) => sort(n.children))
  }
  sort(roots)
  return roots
}

/** Unit types allowed directly under a parent of the given type (mirrors OrgUnitService.CheckPlacement). */
export function childTypesFor(parent: OrgUnitType): OrgUnitType[] {
  if (parent === 'Team') return []
  return unitTypeOrder.filter((t) => t !== 'Organization' && unitTypeOrder.indexOf(t) > unitTypeOrder.indexOf(parent))
}

/** IDs of a unit and everything below it. */
export function subtreeIds(units: OrgUnit[], rootId: number): Set<number> {
  const ids = new Set<number>([rootId])
  let added = true
  while (added) {
    added = false
    for (const u of units) {
      if (u.parentId != null && ids.has(u.parentId) && !ids.has(u.orgUnitId)) {
        ids.add(u.orgUnitId)
        added = true
      }
    }
  }
  return ids
}
