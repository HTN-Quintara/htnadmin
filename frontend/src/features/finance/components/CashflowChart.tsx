import { formatPeso, formatPesoCompact } from '@/lib/format'
import type { CashflowPoint } from '../schemas'

const WIDTH = 720
const HEIGHT = 200
const PAD = { top: 12, right: 8, bottom: 26, left: 56 }

/**
 * Money in and out per month, hand-rolled in SVG — the app carries no charting library and every other visual here
 * (the stepper, the progress bar, the QR) is drawn the same way.
 * <p>
 * Colour is never the only signal: the bars have `<title>` tooltips, the chart has a summary label, and the same
 * numbers follow in a visually-hidden table so a screen reader gets the data rather than a shape.
 */
export function CashflowChart({ points }: { points: CashflowPoint[] }) {
  if (points.length === 0) return null

  const max = Math.max(...points.map((p) => Math.max(p.moneyIn, p.moneyOut)), 1)
  const innerWidth = WIDTH - PAD.left - PAD.right
  const innerHeight = HEIGHT - PAD.top - PAD.bottom
  const slot = innerWidth / points.length
  const barWidth = Math.max(4, Math.min(18, slot / 3))
  const y = (value: number) => PAD.top + innerHeight - (value / max) * innerHeight
  const ticks = [0, max / 2, max]

  const totalIn = points.reduce((sum, p) => sum + p.moneyIn, 0)
  const totalOut = points.reduce((sum, p) => sum + p.moneyOut, 0)

  return (
    <div className="flex flex-col gap-3">
      <div className="flex items-center gap-4 text-xs text-gray-600 dark:text-gray-400">
        <span className="inline-flex items-center gap-1.5">
          <span className="size-2.5 rounded-sm bg-teal-600 dark:bg-teal-400" aria-hidden="true" />
          Money in
        </span>
        <span className="inline-flex items-center gap-1.5">
          <span className="size-2.5 rounded-sm bg-brand-600 dark:bg-brand-300" aria-hidden="true" />
          Money out
        </span>
      </div>
      <svg
        viewBox={`0 0 ${WIDTH} ${HEIGHT}`}
        className="h-48 w-full"
        role="img"
        aria-label={`Money in and out by month. ${formatPeso(totalIn)} in, ${formatPeso(totalOut)} out over ${points.length} ${points.length === 1 ? 'month' : 'months'}.`}
      >
        {ticks.map((tick) => (
          <g key={tick}>
            <line
              x1={PAD.left}
              x2={WIDTH - PAD.right}
              y1={y(tick)}
              y2={y(tick)}
              className="stroke-gray-200 dark:stroke-white/10"
              strokeWidth="1"
            />
            <text
              x={PAD.left - 8}
              y={y(tick) + 4}
              textAnchor="end"
              className="fill-gray-500 text-[10px] tabular-nums dark:fill-gray-400"
            >
              {formatPesoCompact(tick)}
            </text>
          </g>
        ))}
        {points.map((point, i) => {
          const centre = PAD.left + slot * i + slot / 2
          return (
            <g key={point.period}>
              <rect
                x={centre - barWidth - 1}
                y={y(point.moneyIn)}
                width={barWidth}
                height={Math.max(1, PAD.top + innerHeight - y(point.moneyIn))}
                rx="2"
                className="fill-teal-600 dark:fill-teal-400"
              >
                <title>{`${monthLabel(point.period)}: ${formatPeso(point.moneyIn)} in`}</title>
              </rect>
              <rect
                x={centre + 1}
                y={y(point.moneyOut)}
                width={barWidth}
                height={Math.max(1, PAD.top + innerHeight - y(point.moneyOut))}
                rx="2"
                className="fill-brand-600 dark:fill-brand-300"
              >
                <title>{`${monthLabel(point.period)}: ${formatPeso(point.moneyOut)} out`}</title>
              </rect>
              <text
                x={centre}
                y={HEIGHT - 8}
                textAnchor="middle"
                className="fill-gray-500 text-[10px] dark:fill-gray-400"
              >
                {shortMonth(point.period)}
              </text>
            </g>
          )
        })}
      </svg>
      <table className="sr-only">
        <caption>Money in and out by month</caption>
        <thead>
          <tr>
            <th scope="col">Month</th>
            <th scope="col">Money in</th>
            <th scope="col">Money out</th>
            <th scope="col">Net</th>
          </tr>
        </thead>
        <tbody>
          {points.map((p) => (
            <tr key={p.period}>
              <th scope="row">{monthLabel(p.period)}</th>
              <td>{formatPeso(p.moneyIn)}</td>
              <td>{formatPeso(p.moneyOut)}</td>
              <td>{formatPeso(p.net)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

function monthLabel(period: string): string {
  const d = new Date(`${period}T00:00:00`)
  return Number.isNaN(d.getTime()) ? period : d.toLocaleDateString('en-PH', { month: 'long', year: 'numeric' })
}

function shortMonth(period: string): string {
  const d = new Date(`${period}T00:00:00`)
  return Number.isNaN(d.getTime()) ? period : d.toLocaleDateString('en-PH', { month: 'short' })
}
