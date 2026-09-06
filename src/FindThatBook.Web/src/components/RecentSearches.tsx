import HistoryIcon from '@mui/icons-material/History';
import RefreshIcon from '@mui/icons-material/Refresh';
import { Box, Button, Chip, Paper, Stack, Tooltip, Typography } from '@mui/material';
import { observer } from 'mobx-react-lite';
import type { SearchStore } from '../stores/SearchStore';

interface Props {
  store: SearchStore;
}

const countLabel = (count: number) => (count === 1 ? '1 result' : `${count} results`);

/**
 * The last few completed searches, restorable without touching the API.
 *
 * Selecting one replays the stored response from memory, so it is instant and costs no
 * model or catalogue call. Because a replayed answer is indistinguishable from a fresh one
 * on screen, the active entry is highlighted and a note offers to run the search again.
 */
export const RecentSearches = observer(({ store }: Props) => {
  if (store.history.length === 0) {
    return null;
  }

  return (
    <Paper variant="outlined" sx={{ p: 1.5 }}>
      <Stack
        direction={{ xs: 'column', sm: 'row' }}
        spacing={1}
        sx={{ alignItems: { xs: 'flex-start', sm: 'center' } }}
      >
        <Stack direction="row" spacing={0.75} sx={{ alignItems: 'center', flexShrink: 0 }}>
          <HistoryIcon fontSize="small" color="action" />
          <Typography variant="caption" color="text.secondary">
            Recent
          </Typography>
        </Stack>

        <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap', flexGrow: 1 }}>
          {store.history.map((entry) => {
            const active = store.isShowing(entry);

            return (
              <Tooltip
                key={`${entry.query}-${entry.searchedAt}`}
                title={`${countLabel(entry.response.results.length)} — shown instantly, no new search`}
              >
                <Chip
                  size="small"
                  label={entry.query}
                  onClick={() => store.showFromHistory(entry)}
                  color={active ? 'primary' : 'default'}
                  variant={active ? 'filled' : 'outlined'}
                  disabled={store.isSearching}
                  sx={{ maxWidth: 260 }}
                />
              </Tooltip>
            );
          })}
        </Stack>

        <Button
          size="small"
          variant="text"
          onClick={store.clearHistory}
          disabled={store.isSearching}
          sx={{ textTransform: 'none', flexShrink: 0 }}
        >
          Clear
        </Button>
      </Stack>

      {store.viewingFromHistory ? (
        <Box sx={{ mt: 1.25, pt: 1.25, borderTop: 1, borderColor: 'divider' }}>
          <Stack
            direction="row"
            spacing={1}
            sx={{ alignItems: 'center', flexWrap: 'wrap', rowGap: 0.5 }}
          >
            <Typography variant="caption" color="text.secondary">
              Showing a saved result from earlier in this session.
            </Typography>
            <Button
              size="small"
              variant="text"
              startIcon={<RefreshIcon />}
              onClick={() => void store.search(store.query)}
              sx={{ textTransform: 'none' }}
            >
              Search again
            </Button>
          </Stack>
        </Box>
      ) : null}
    </Paper>
  );
});
