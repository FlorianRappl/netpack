import * as React from 'react';
import { render } from 'react-dom';
import styled from '@emotion/styled';
import smiley from './smiley.jpg';

const Wrapper = styled.div`
  font-family: sans-serif;
  max-width: 800px;
  margin: 2em auto;
`;

const Header = styled.h2`
  font-size: 1.5em;
  color: #333;
`;

const Page = React.lazy(() => import('./Page'));

const App = () => {
  const [showPage, setShowPage] = React.useState(false);

  return (
    <React.Suspense fallback={<b>Loading ...</b>}>
      <div className="main-content">
        <Header>Let's talk about smileys</Header>
        <p>More about smileys can be found here ...</p>
        <Wrapper>
          <img src={smiley} alt="A classic smiley" />
        </Wrapper>
        <p>
          <button onClick={() => setShowPage(!showPage)}>Toggle page</button>
        </p>
      </div>
      {showPage && <Page />}
    </React.Suspense>
  );
};

render(<App />, document.querySelector('#app'));
