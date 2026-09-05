import SearchOffIcon from '@mui/icons-material/SearchOff';
import { Box, Paper, Skeleton, Stack, Typography } from '@mui/material';
import { observer } from 'mobx-react-lite';
import type { SearchStore } from '../stores/SearchStore';
import { ResultCard } from './ResultCard';

interface Props {
  store: SearchStore;
}

const LoadingCards = () => {
  return (
    <Stack spacing={2} aria-busy="true" aria-label="Searching">
      {[0, 1, 2].map((index) => (
        <Paper key={index} variant="outlined" sx={{ p: 2 }}>
          <Stack direction="row" spacing={2}>
            <Skeleton variant="circular" width={30} height={30} />
            <Skeleton variant="rounded" width={72} height={104} />
            <Box sx={{ flexGrow: 1 }}>
              <Skeleton variant="text" width="55%" height={28} />
              <Skeleton variant="text" width="35%" />
              <Skeleton variant="rounded" height={54} sx={{ mt: 1.5 }} />
            </Box>
          </Stack>
        </Paper>
      ))}
    </Stack>
  );
};

/** A search that worked and matched nothing is a normal answer, not a failure. */
const NoMatches = ({ query }: { query: string }) => {
  return (
    <Paper variant="outlined" sx={{ p: 4, textAlign: 'center' }}>
      <SearchOffIcon sx={{ fontSize: 40, color: 'text.disabled' }} />
      <Typography variant="h2" sx={{ mt: 1 }}>
        No matches for “{query}”
      </Typography>
      <Typography variant="body2" color="text.secondary" sx={{ mt: 1, maxWidth: 460, mx: 'auto' }}>
        The catalogue was searched successfully but returned nothing. Try fewer words, or the
        author&apos;s surname on its own.
      </Typography>
    </Paper>
  );
};

export const ResultsList = observer(({ store }: Props) => {
  if (store.isSearching) {
    return <LoadingCards />;
  }

  if (store.foundNothing) {
    return <NoMatches query={store.response?.query ?? store.query} />;
  }

  const results = store.response?.results ?? [];

  if (results.length === 0) {
    return null;
  }

  return (
    <Stack spacing={2}>
      <Typography variant="body2" color="text.secondary">
        {results.length} {results.length === 1 ? 'candidate' : 'candidates'}, best match first
      </Typography>

      {results.map((candidate, index) => (
        <ResultCard
          key={candidate.openLibraryKey ?? `${candidate.title}-${index}`}
          candidate={candidate}
          rank={index + 1}
          rankingSource={store.rankingSource}
        />
      ))}
    </Stack>
  );
});
