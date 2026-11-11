// Keep the actual release on records; only selection uses card-rule groups.
export function cardVersion(version, groups = {}) {
  return Object.hasOwn(groups, version) ? groups[version] : version;
}

export function selectedCardVersions(selected, groups = {}) {
  return [...new Set((Array.isArray(selected) ? selected : selected ? [selected] : [])
    .map(version => cardVersion(version, groups)))];
}

// Empty selection means all versions. Historical scalar links select their group too.
export function matchesVersion(version, selected, groups = {}) {
  const values = selectedCardVersions(selected, groups);
  return values.length === 0 || values.includes(cardVersion(version, groups));
}
