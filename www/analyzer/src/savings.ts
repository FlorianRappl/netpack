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
  'duplicate-package': 'Duplicate package versions',
  'oversized-bundle': 'Oversized bundle',
  'heavy-module': 'Dominant module',
  'inline-asset': 'Inline this asset',
  'stop-inlining-asset': 'Stop inlining asset',
}

// A distinct hue per kind, used for the summary stack, legend and card marker.
let KIND_COLOR: Record<string, string> = {
  'duplicate-module': '#b00020',
  'duplicate-package': '#d32f2f',
  'stop-inlining-asset': '#e64a19',
  'oversized-bundle': '#f9a825',
  'heavy-module': '#6a1b9a',
  'inline-small-chunk': '#1976d2',
  'inline-asset': '#1565c0',
  'merge-orphan-chunk': '#00897b',
}

let kindColor = (kind: string): string => KIND_COLOR[kind] || '#6b7280'
let kindLabel = (kind: string): string => KIND_LABEL[kind] || kind

let renderRecommendation = (recommendation: SavingsRecommendation, maxAbs: number): HTMLElement => {
  let card = document.createElement('div')
  card.className = styles.rec

  let impact: string[] = []
  if (recommendation.bytes > 0) impact.push(`saves ${bytesToText(recommendation.bytes)}`)
  else if (recommendation.bytes < 0) impact.push(`adds ${bytesToText(-recommendation.bytes)}`)
  if (recommendation.requests > 0) impact.push(`${recommendation.requests} fewer request${recommendation.requests === 1 ? '' : 's'}`)
  else if (recommendation.requests < 0) impact.push(`${-recommendation.requests} more request${recommendation.requests === -1 ? '' : 's'}`)

  let chips = [...(recommendation.modules || []), ...(recommendation.bundles || [])]
    .map((c) => `<code>${textToHTML(c)}</code>`)
    .join(' ')

  // A small impact bar: green when it saves bytes, amber when it's a trade-off.
  let abs = Math.abs(recommendation.bytes)
  let width = maxAbs > 0 && abs > 0 ? Math.max(3, Math.round((abs / maxAbs) * 100)) : 0
  let barColor = recommendation.bytes >= 0 ? '#2e7d32' : '#f9a825'
  let bar = width > 0
    ? `<div class="${styles.bar}"><span class="${styles.fill}" style="width:${width}%;background:${barColor}"></span></div>`
    : ''

  card.innerHTML = ''
    + `<div class="${styles.head}">`
    + `<span class="${styles.marker}" style="background:${kindColor(recommendation.kind)}"></span>`
    + `<span class="${styles.badge} ${severityClass(recommendation.severity)}">${textToHTML(recommendation.severity || 'low')}</span>`
    + `<span class="${styles.kind}">${textToHTML(kindLabel(recommendation.kind))}</span>`
    + (impact.length ? `<span class="${styles.impact}">${textToHTML(impact.join(' · '))}</span>` : '')
    + '</div>'
    + bar
    + `<p class="${styles.message}">${textToHTML(recommendation.message)}</p>`
    + (chips ? `<div class="${styles.bundles}">${chips}</div>` : '')

  return card
}

// A stacked bar of recommendations grouped by kind, sized by count, with a legend.
let renderSummary = (recommendations: SavingsRecommendation[], potentialBytes: number): HTMLElement => {
  let counts: Record<string, number> = {}
  for (let r of recommendations) counts[r.kind] = (counts[r.kind] || 0) + 1

  let kinds = Object.keys(counts).sort((a, b) => counts[b] - counts[a])
  let total = recommendations.length

  let segments = kinds
    .map((k) => `<span class="${styles.seg}" style="width:${(counts[k] / total) * 100}%;background:${kindColor(k)}" title="${textToHTML(kindLabel(k))}: ${counts[k]}"></span>`)
    .join('')

  let legend = kinds
    .map((k) => `<span class="${styles.legendItem}"><i style="background:${kindColor(k)}"></i>${textToHTML(kindLabel(k))} <b>${counts[k]}</b></span>`)
    .join('')

  let el = document.createElement('div')
  el.className = styles.summary
  el.innerHTML = ''
    + `<div class="${styles.total}">`
    + (potentialBytes > 0 ? `<strong>≈ ${bytesToText(potentialBytes)}</strong> of duplicated code recoverable · ` : '')
    + `${total} recommendation${total === 1 ? '' : 's'}`
    + '</div>'
    + `<div class="${styles.stack}">${segments}</div>`
    + `<div class="${styles.legend}">${legend}</div>`
  return el
}

export let createSavings = (metafile: Metafile): HTMLElement => {
  let el = document.createElement('div')
  el.className = styles.savings

  let savings = metafile.savings
  let recommendations = (savings && savings.recommendations) || []

  if (!savings || recommendations.length === 0) {
    let header = document.createElement('div')
    header.className = styles.header
    header.innerHTML = `<p class="${styles.ok}">✅ Your bundles are well shaped — no optimization opportunities found.</p>`
    el.append(header)
    return el
  }

  let sorted = recommendations
    .slice()
    .sort((a, b) => severityRank(b.severity) - severityRank(a.severity) || b.bytes - a.bytes)

  let maxAbs = 0
  for (let r of recommendations) maxAbs = Math.max(maxAbs, Math.abs(r.bytes))

  el.append(renderSummary(recommendations, savings.potentialBytes))

  for (let recommendation of sorted) {
    el.append(renderRecommendation(recommendation, maxAbs))
  }

  return el
}
