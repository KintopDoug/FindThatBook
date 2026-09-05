import SearchIcon from '@mui/icons-material/Search';
import { Box, Button, CircularProgress, InputAdornment, Stack, TextField, Typography } from '@mui/material';
import { observer } from 'mobx-react-lite';
import type { SearchStore } from '../stores/SearchStore';

const EXAMPLES = ['tale two cities', 'dickens', 'that hobbit book by tolkein'];

interface Props {
  store: SearchStore;
}

export const SearchBar = observer(({ store }: Props) => {
  const submit = (event: React.FormEvent) => {
    event.preventDefault();
    void store.search();
  };

  return (
    <Box component="form" onSubmit={submit}>
      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.5}>
        <TextField
          fullWidth
          autoFocus
          label="Describe the book"
          placeholder="a title, an author, or just what you remember"
          value={store.query}
          onChange={(event) => store.setQuery(event.target.value)}
          disabled={store.isSearching}
          slotProps={{
            input: {
              startAdornment: (
                <InputAdornment position="start">
                  <SearchIcon color="action" />
                </InputAdornment>
              ),
            },
          }}
        />
        <Button
          type="submit"
          variant="contained"
          size="large"
          disabled={store.isSearching || store.query.trim().length === 0}
          sx={{ minWidth: 130, whiteSpace: 'nowrap' }}
          startIcon={store.isSearching ? <CircularProgress size={18} color="inherit" /> : null}
        >
          {store.isSearching ? 'Searching' : 'Search'}
        </Button>
      </Stack>

      <Stack direction="row" spacing={1} sx={{ alignItems: 'center', flexWrap: 'wrap', mt: 1.5 }}>
        <Typography variant="caption" color="text.secondary">
          Try:
        </Typography>
        {EXAMPLES.map((example) => (
          <Button
            key={example}
            size="small"
            variant="text"
            disabled={store.isSearching}
            onClick={() => void store.search(example)}
            sx={{ textTransform: 'none', minWidth: 0 }}
          >
            {example}
          </Button>
        ))}
      </Stack>
    </Box>
  );
});
