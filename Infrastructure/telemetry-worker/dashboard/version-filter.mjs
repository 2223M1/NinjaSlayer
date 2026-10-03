// Empty selection means all versions. Keep scalar query links working too.
export function matchesVersion(version, selected) {
  return Array.isArray(selected) ? selected.length === 0 || selected.includes(version)
    : !selected || selected === version;
}
