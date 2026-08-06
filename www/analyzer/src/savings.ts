import * as styles from './savings.css'
import { Metafile, SavingsRecommendation } from './metafile'
import { textToHTML, bytesToText } from './helpers'

let SEVERITY_RANK: Record<string, number> = {
  high: 3,
  medium: 2,
  low: 1,
}

let severityRank = (severity?: string): number =>
  SEVERITY_RANK[(severity || '').toLowerCase()] ?? 0

let severityClass = (severity?: string): string => {
  switch ((severity || '').toLowerCase()) {
    case 'high': return styles.high
    case 'medium': return styles.medium
    default: return styles.low
  }
}

let KIND_LABEL: Record<string, string> = {
  'duplicate-module': 'Duplicated module',
  'merge-orphan-chunk': 'Orphan shared chunk',
  'inline-small-chunk': 'Over-split chunk',
}

let renderRecommendation = (recommendation: SavingsRecommendation): HTMLElement => {
  let card = document.createElement('div')
  card.className = styles.rec

  let label = KIND_LABEL[recommendation.kind] || recommendation.kind

  let impact: string[] = []
  if (recommendation.bytes > 0) impact.push(`saves ${bytesToText(recommendation.bytes)}`)
  else if (recommendation.bytes < 0) impact.push(`adds ${bytesToText(-recommendation.bytes)}`)
  if (recommendation.requests > 0) impact.push(`${recommendation.requests} fewer request${recommendation.requests === 1 ? '' : 's'}`)

  let bundles = (recommendation.bundles || []).map((b) => `<code>${textToHTML(b)}</code>`).join(' ')

  card.innerHTML = ''
    + `<div class="${styles.head}">`
    + `<span class="${styles.badge} ${severityClass(recommendation.severity)}">${textToHTML(recommendation.severity || 'low')}</span>`
    + `<span class="${styles.kind}">${textToHTML(label)}</span>`
    + (impact.length ? `<span class="${styles.impact}">${textToHTML(impact.join(' · '))}</span>` : '')
    + '</div>'
    + `<p class="${styles.message}">${textToHTML(recommendation.message)}</p>`
    + (bundles ? `<div class="${styles.bundles}">${bundles}</div>` : '')

  return card
}

export let createSavings = (metafile: Metafile): HTMLElement => {
  let el = document.createElement('div')
  el.className = styles.savings

  let savings = metafile.savings
  let recommendations = (savings && savings.recommendations) || []

  let header = document.createElement('div')
  header.className = styles.header

  if (!savings || recommendations.length === 0) {
    header.innerHTML = `<p class="${styles.ok}">✅ Your bundles are well shaped — no optimization opportunities found.</p>`
    el.append(header)
    return el
  }

  let count = recommendations.length
  let waste = savings.potentialBytes > 0
    ? ` — about ${bytesToText(savings.potentialBytes)} of duplicated code could be removed`
    : ''
  header.innerHTML = `<p>${count} recommendation${count === 1 ? '' : 's'} to improve how your code is split${waste}.</p>`
  el.append(header)

  let sorted = recommendations
    .slice()
    .sort((a, b) => severityRank(b.severity) - severityRank(a.severity) || b.bytes - a.bytes)

  for (let recommendation of sorted) {
    el.append(renderRecommendation(recommendation))
  }

  return el
}
