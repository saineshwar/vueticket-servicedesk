/**
 * For select fields whose "default" choice has an empty value (e.g. "All desks", "Same as the parent unit"):
 * show that choice's text instead of an empty box, with the label raised above it.
 */
export const showEmptyOption = { select: { displayEmpty: true }, inputLabel: { shrink: true } } as const
