import * as styles from './audits.css'
import { Metafile, AuditVulnerability } from './metafile'
import { textToHTML } from './helpers'

let SEVERITY_RANK: Record<string, number> = {
  critical: 4,
  high: 3,
  moderate: 2,
  low: 1,
  info: 0,
}

let severityRank = (severity?: string): number =>
  SEVERITY_RANK[(severity || '').toLowerCase()] ?? -1

let severityClass = (severity?: string): string => {
  switch ((severity || '').toLowerCase()) {
    case 'critical': return styles.critical
    case 'high': return styles.high
    case 'moderate': return styles.moderate
    case 'low': return styles.low
    default: return styles.info
  }
}

let renderVulnerability = (vulnerability: AuditVulnerability): HTMLElement => {
  let card = document.createElement('div')
  card.className = styles.vuln

  let titleText = textToHTML(vulnerability.title || 'Advisory')
  let title = vulnerability.url
    ? `<a href="${textToHTML(vulnerability.url)}" target="_blank" rel="noopener noreferrer">${titleText}</a>`
    : titleText

  let meta: string[] = []
  if (vulnerability.vulnerableVersions) meta.push(`affected: <code>${textToHTML(vulnerability.vulnerableVersions)}</code>`)
  if (typeof vulnerability.cvssScore === 'number') meta.push(`CVSS ${vulnerability.cvssScore.toFixed(1)}`)
  if (vulnerability.cwe && vulnerability.cwe.length) meta.push(vulnerability.cwe.map(textToHTML).join(', '))
  if (vulnerability.id) meta.push(`advisory ${textToHTML(vulnerability.id)}`)

  card.innerHTML = ''
    + `<div class="${styles.vulnHead}">`
    + `<span class="${styles.badge} ${severityClass(vulnerability.severity)}">${textToHTML(vulnerability.severity || 'unknown')}</span>`
    + `<span class="${styles.vulnTitle}">${title}</span>`
    + '</div>'
    + (meta.length ? `<div class="${styles.meta}">${meta.join(' · ')}</div>` : '')

  return card
}

export let createAudits = (metafile: Metafile): HTMLElement => {
  let el = document.createElement('div')
  el.className = styles.audits

  let audit = metafile.audit

  if (!audit) {
    el.innerHTML = `<p class="${styles.empty}">No audit data available. Run <code>netpack analyze</code> with dependency auditing enabled.</p>`
    return el
  }

  let vulnerabilities = audit.vulnerabilities || []

  let header = document.createElement('div')
  header.className = styles.header

  if (audit.error) {
    header.innerHTML = `<p class="${styles.error}">⚠️ The audit could not be completed: ${textToHTML(audit.error)}</p>`
  } else if (vulnerabilities.length === 0) {
    header.innerHTML = `<p class="${styles.ok}">✅ No known vulnerabilities in ${audit.checked} package${audit.checked === 1 ? '' : 's'}.</p>`
  } else {
    let summary = audit.summary || {}
    let badges = Object.keys(summary)
      .sort((a, b) => severityRank(b) - severityRank(a))
      .map((key) => `<span class="${styles.badge} ${severityClass(key)}">${summary[key]} ${textToHTML(key)}</span>`)
      .join(' ')
    let count = vulnerabilities.length
    header.innerHTML = `<p>${count} advisor${count === 1 ? 'y' : 'ies'} across ${audit.checked} package${audit.checked === 1 ? '' : 's'} ${badges}</p>`
  }

  el.append(header)

  // Group findings by package (name + version), sorted by package.
  let groups: Record<string, AuditVulnerability[]> = {}
  for (let vulnerability of vulnerabilities) {
    let key = (vulnerability.name || '?') + '@' + (vulnerability.version || '?')
    ;(groups[key] || (groups[key] = [])).push(vulnerability)
  }

  for (let key of Object.keys(groups).sort()) {
    let items = groups[key].sort((a, b) => severityRank(b.severity) - severityRank(a.severity))
    let worst = items.length ? items[0].severity : undefined

    let group = document.createElement('section')
    group.className = styles.package

    let title = document.createElement('h3')
    title.className = styles.packageName
    title.innerHTML = ''
      + `<span class="${styles.dot} ${severityClass(worst)}"></span>`
      + `<span>${textToHTML(key)}</span>`
      + `<span class="${styles.count}">${items.length} finding${items.length === 1 ? '' : 's'}</span>`
    group.append(title)

    for (let vulnerability of items) {
      group.append(renderVulnerability(vulnerability))
    }

    el.append(group)
  }

  return el
}
