import foo from './foo.mjs';

const magic = () => {
    console.log('Calling foo');
    foo();
    console.log('Called foo');
};

export { magic };

export default function() {
    console.log('Calling magic');
    magic();
    console.log('Called magic');
}
