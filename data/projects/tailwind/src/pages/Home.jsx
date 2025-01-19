import * as React from "react";
import { CSSTransition } from "react-transition-group";
import stockPhoto from "../stock-photo.jpg";

function Home() {
  const [showVertical, setShowVertical] = React.useState(true);
  const [showHorizontal, setShowHorizontal] = React.useState(false);

  return (
    <section className="p-6">
      <h2 className="text-2xl font-light text-gray-900 leading-none mb-6">
        Home
      </h2>
      <p>This is the homepage.</p>

      <div className="my-6">
        {showVertical && (
          <div
            className="card state-1 max-w-sm rounded overflow-hidden shadow-lg"
            onClick={() => setShowHorizontal(true)}
          >
            <img
              className="w-full"
              src={stockPhoto}
              alt="Woman of color weighing all options."
            />
            <div className="px-6 py-4">
              <h3 className="text-gray-900 font-bold text-xl mb-2">
                Contemplating Invisible Rocks
              </h3>
              <span className="inline-block bg-gray-200 rounded-full px-3 py-1 text-sm font-semibold text-gray-700 mr-2">
                #philosophy
              </span>
              <span className="inline-block bg-gray-200 rounded-full px-3 py-1 text-sm font-semibold text-gray-700">
                #think piece
              </span>
            </div>
          </div>
        )}

        <CSSTransition
          in={showHorizontal}
          timeout={350}
          unmountOnExit
          onEnter={() => setShowVertical(false)}
          onExited={() => setShowVertical(true)}
        >
          <div
            className="card state-2 max-w-sm w-full lg:max-w-full lg:flex"
            variant="primary"
            onClick={() => setShowHorizontal(false)}
          >
            <div
              className="h-48 lg:h-auto lg:w-48 flex-none bg-cover rounded-t lg:rounded-t-none lg:rounded-l text-center overflow-hidden"
              style={{ backgroundImage: `url(${stockPhoto})` }}
              title="Woman holding a mug"
            ></div>
            <div className="border-r border-b border-l border-gray-400 lg:border-l-0 lg:border-t lg:border-gray-400 bg-white rounded-b lg:rounded-b-none lg:rounded-r p-4 flex flex-col justify-between leading-normal">
              <div className="mb-2">
                <h3 className="text-gray-900 font-bold text-xl mb-2">
                  Contemplating Invisible Rocks
                </h3>
                <p className="text-gray-700 text-base">
                  Lorem ipsum dolor sit amet, consectetur adipisicing elit.
                  Voluptatibus quia, nulla! Maiores et perferendis eaque,
                  exercitationem praesentium nihil.
                </p>
                <div className="py-4">
                  <span className="inline-block bg-gray-200 rounded-full px-3 py-1 text-sm font-semibold text-gray-700 mr-2">
                    #philosophy
                  </span>
                  <span className="inline-block bg-gray-200 rounded-full px-3 py-1 text-sm font-semibold text-gray-700">
                    #think piece
                  </span>
                </div>
              </div>
              <div className="flex items-center">
                <img
                  className="w-10 h-10 rounded-full mr-4"
                  src={stockPhoto}
                  alt="Woman of color weighing all options."
                />
                <div className="text-sm">
                  <p className="text-gray-900 leading-none">
                    Testing CSS Transitions
                  </p>
                  <p className="text-gray-600">Aug 18</p>
                </div>
              </div>
            </div>
          </div>
        </CSSTransition>
      </div>
    </section>
  );
}

export default Home;
