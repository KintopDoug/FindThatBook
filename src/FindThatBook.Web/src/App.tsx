import { Box, Container, Stack, Typography } from '@mui/material';
import { observer } from 'mobx-react-lite';
import { ErrorNotice } from './components/ErrorNotice';
import { InterpretationPanel } from './components/InterpretationPanel';
import { ResultsList } from './components/ResultsList';
import { SearchBar } from './components/SearchBar';
import { searchStore } from './stores/SearchStore';

export const App = observer(() => {
  const store = searchStore;

  return (
    <Container maxWidth="md" sx={{ py: { xs: 3, sm: 6 } }}>
      <Stack spacing={3}>
        <Box component="header">
          <Typography variant="h1" component="h1">
            Find That Book
          </Typography>
          <Typography variant="body1" color="text.secondary" sx={{ mt: 0.5 }}>
            Describe a book however you remember it. Partial titles, misspelled authors, and
            vague hints all work.
          </Typography>
        </Box>

        <SearchBar store={store} />

        <Box component="main" aria-live="polite">
          <Stack spacing={3}>
            {store.error ? (
              <ErrorNotice error={store.error} onRetry={() => void store.search()} />
            ) : null}

            {store.response && !store.isSearching ? <InterpretationPanel store={store} /> : null}

            <ResultsList store={store} />
          </Stack>
        </Box>

        <Box component="footer" sx={{ pt: 2 }}>
          <Typography variant="caption" color="text.secondary">
            Book data from Open Library.
          </Typography>
        </Box>
      </Stack>
    </Container>
  );
});
