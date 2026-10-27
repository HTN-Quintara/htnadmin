import { createBrowserRouter, Navigate, type RouteObject } from 'react-router'
import { RedirectIfSignedIn, RequireAuth } from './auth/RequireAuth'
import { RequirePolicy } from './auth/RequirePolicy'
import { Policies } from './auth/policies'
import { AppLayout } from './layouts/AppLayout'
import { KioskLayout } from './layouts/KioskLayout'
import { ComingSoonPage } from './pages/ComingSoonPage'
import { NotFoundPage } from './pages/NotFoundPage'
import { RouteErrorPage } from './pages/RouteErrorPage'

const comingSoon = (path: string, title: string, description: string): RouteObject => ({
  path,
  element: <ComingSoonPage title={title} description={description} />,
})

export const routes: RouteObject[] = [
  {
    errorElement: <RouteErrorPage />,
    children: [
      // Public pages: no app shell and no sign-in (the API is anonymous and rate limited).
      {
        path: '/r/:slug',
        lazy: async () => ({ Component: (await import('@/features/registrations/public/PublicRegistrationPage')).PublicRegistrationPage }),
      },
      {
        path: '/pay/:token',
        lazy: async () => ({ Component: (await import('@/features/registrations/public/ProofUploadPage')).ProofUploadPage }),
      },
      {
        element: <RedirectIfSignedIn />,
        children: [
          { path: '/login', lazy: async () => ({ Component: (await import('@/features/auth/LoginPage')).LoginPage }) },
          { path: '/forgot', lazy: async () => ({ Component: (await import('@/features/auth/ForgotPasswordPage')).ForgotPasswordPage }) },
          { path: '/reset/:token', lazy: async () => ({ Component: (await import('@/features/auth/ResetPasswordPage')).ResetPasswordPage }) },
          { path: '/invite/:token', lazy: async () => ({ Component: (await import('@/features/auth/InvitePage')).InvitePage }) },
        ],
      },
      {
        element: <RequireAuth allowTwoFactorSetup />,
        children: [
          {
            path: '/setup-2fa',
            lazy: async () => ({ Component: (await import('@/features/auth/SetupTwoFactorPage')).SetupTwoFactorPage }),
          },
        ],
      },
      {
        element: <RequireAuth />,
        children: [
          {
            // Kiosk screens: no sidebar. Any signed-in role can scan (the API checks CheckIn.Scan too).
            element: <KioskLayout />,
            children: [
              {
                element: <RequirePolicy policy={Policies.CheckInScan} />,
                children: [
                  { path: '/events/:eventId/check-in/scan', lazy: async () => ({ Component: (await import('@/features/checkin/ScanPage')).ScanPage }) },
                  { path: '/events/:eventId/check-in/desk', lazy: async () => ({ Component: (await import('@/features/checkin/DeskPage')).DeskPage }) },
                ],
              },
            ],
          },
          {
            element: <AppLayout />,
            children: [
              { index: true, lazy: async () => ({ Component: (await import('@/features/dashboard/DashboardPage')).DashboardPage }) },
              {
                path: '/people',
                element: <RequirePolicy policy={Policies.PeopleView} />,
                children: [
                  { index: true, lazy: async () => ({ Component: (await import('@/features/organization/PeoplePage')).PeoplePage }) },
                  {
                    path: 'positions',
                    lazy: async () => ({ Component: (await import('@/features/organization/PositionsPage')).PositionsPage }),
                  },
                ],
              },
              { path: '/settings', lazy: async () => ({ Component: (await import('@/features/settings/SettingsPage')).SettingsPage }) },
              {
                path: '/settings/permissions',
                lazy: async () => ({ Component: (await import('@/features/settings/PermissionsPage')).PermissionsPage }),
              },
              {
                path: '/events',
                element: <RequirePolicy policy={Policies.EventsView} />,
                children: [
                  { index: true, lazy: async () => ({ Component: (await import('@/features/events/EventsPage')).EventsPage }) },
                  {
                    path: 'new',
                    element: <RequirePolicy policy={Policies.EventsManage} />,
                    children: [{ index: true, lazy: async () => ({ Component: (await import('@/features/events/CreateEventPage')).CreateEventPage }) }],
                  },
                  {
                    path: ':eventId',
                    lazy: async () => ({ Component: (await import('@/features/events/EventLayout')).EventLayout }),
                    children: [
                      { index: true, element: <Navigate to="details" replace /> },
                      { path: 'details', lazy: async () => ({ Component: (await import('@/features/events/EventDetailsPage')).EventDetailsPage }) },
                      { path: 'agenda', lazy: async () => ({ Component: (await import('@/features/events/agenda/AgendaPage')).AgendaPage }) },
                      {
                        path: 'committees',
                        children: [{ index: true, lazy: async () => ({ Component: (await import('@/features/committees/CommitteesPage')).CommitteesPage }) }],
                      },
                      {
                        path: 'tasks',
                        element: <RequirePolicy policy={Policies.TasksViewBoard} />,
                        children: [
                          {
                            // The task dialog is a child route of the board, so it is deep-linkable.
                            lazy: async () => ({ Component: (await import('@/features/tasks/TaskBoardPage')).TaskBoardPage }),
                            children: [
                              { index: true, element: null },
                              {
                                path: ':taskId',
                                lazy: async () => ({ Component: (await import('@/features/tasks/TaskDialogRoute')).TaskDialogRoute }),
                              },
                            ],
                          },
                        ],
                      },
                      {
                        path: 'registrations',
                        element: <RequirePolicy policy={Policies.RegistrationsManage} />,
                        children: [
                          { index: true, lazy: async () => ({ Component: (await import('@/features/registrations/RegistrationsPage')).RegistrationsPage }) },
                        ],
                      },
                      {
                        path: 'emails',
                        element: <RequirePolicy policy={Policies.EventsManage} />,
                        children: [
                          { index: true, lazy: async () => ({ Component: (await import('@/features/registrations/EmailSettingsPage')).EmailSettingsPage }) },
                        ],
                      },
                      {
                        path: 'sponsors',
                        element: <RequirePolicy policy={Policies.SponsorsManage} />,
                        children: [
                          { index: true, lazy: async () => ({ Component: (await import('@/features/sponsors/SponsorsPage')).SponsorsPage }) },
                        ],
                      },
                      {
                        path: 'leads',
                        element: <RequirePolicy policy={Policies.LeadsManage} />,
                        children: [
                          { index: true, lazy: async () => ({ Component: (await import('@/features/sponsors/leads/LeadsPage')).LeadsPage }) },
                        ],
                      },
                      {
                        path: 'check-in',
                        element: <RequirePolicy policy={Policies.CheckInScan} />,
                        children: [{ index: true, lazy: async () => ({ Component: (await import('@/features/checkin/CheckInPage')).CheckInPage }) }],
                      },
                      { path: '*', element: <NotFoundPage /> },
                    ],
                  },
                ],
              },
              comingSoon('/directory', 'Directory', 'Agencies, companies, universities, non-profits and suppliers.'),
              comingSoon('/store', 'Merch store', 'Leftover stock and pop-up stores.'),
              {
                // Board only, plus the one finance assistant. The API answers 404 to everyone else, so the module's
                // existence is not revealed; this guard only saves them a pointless round trip.
                path: '/finance',
                element: <RequirePolicy policy={Policies.FinanceView} />,
                children: [
                  {
                    lazy: async () => ({ Component: (await import('@/features/finance/FinanceLayout')).FinanceLayout }),
                    children: [
                      { index: true, lazy: async () => ({ Component: (await import('@/features/finance/FinancePage')).FinancePage }) },
                      { path: 'budgets/:eventId', lazy: async () => ({ Component: (await import('@/features/finance/BudgetPage')).BudgetPage }) },
                      { path: 'requests', lazy: async () => ({ Component: (await import('@/features/finance/RequestsPage')).RequestsPage }) },
                      { path: '*', element: <NotFoundPage /> },
                    ],
                  },
                ],
              },
              { path: '/my-tasks', lazy: async () => ({ Component: (await import('@/features/tasks/MyTasksPage')).MyTasksPage }) },
              { path: '/tasks', element: <Navigate to="/my-tasks" replace /> },
              {
                path: '/leaderboard',
                element: <RequirePolicy policy={Policies.LeaderboardView} />,
                children: [{ index: true, lazy: async () => ({ Component: (await import('@/features/leaderboard/LeaderboardPage')).LeaderboardPage }) }],
              },
              { path: '*', element: <NotFoundPage /> },
            ],
          },
        ],
      },
    ],
  },
]

export function createRouter() {
  return createBrowserRouter(routes)
}
