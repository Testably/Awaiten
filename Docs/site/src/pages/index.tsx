import React, {type ReactElement, type ReactNode} from 'react';
import clsx from 'clsx';
import Link from '@docusaurus/Link';
import useBaseUrl from '@docusaurus/useBaseUrl';
import useDocusaurusContext from '@docusaurus/useDocusaurusContext';
import Layout from '@theme/Layout';
import PackageInstall from '@site/src/components/PackageInstall';
import styles from './index.module.css';

function Step({warm, last, children}: {warm?: boolean; last?: boolean; children: ReactNode}): ReactElement {
  return (
    <div className={styles.step}>
      <div className={styles.rail} aria-hidden="true">
        <span className={clsx(styles.node, warm && styles.nodeWarm)} />
        {!last && <span className={styles.line} />}
      </div>
      <div className={styles.stepBody}>{children}</div>
    </div>
  );
}

const Kw = ({children}: {children: ReactNode}) => <span className={styles.kw}>{children}</span>;
const Attr = ({children}: {children: ReactNode}) => <span className={styles.attr}>{children}</span>;
const Type = ({children}: {children: ReactNode}) => <span className={styles.type}>{children}</span>;
const Comment = ({children}: {children: ReactNode}) => <span className={styles.comment}>{children}</span>;

const diagnostics = ['AWT119', 'AWT101', 'AWT102', 'AWT105'];

export default function Home(): ReactElement {
  const {siteConfig} = useDocusaurusContext();
  return (
    <Layout description={siteConfig.tagline}>
      <header className={styles.hero}>
        <div className={styles.heroInner}>
          <img className={styles.heroLogo} src={useBaseUrl('/img/logo-dark.svg')} alt="" />
          <h1 className={styles.heroTitle}>
            Nothing gets served before the machine is <span className={styles.warm}>warm</span>.
          </h1>
          <p className={styles.heroText}>
            The async-first dependency injection container for .NET. Awaiten follows services that need asynchronous
            setup through your object graph and refuses to compile code that would use one too early.
          </p>
          <div className={styles.actions}>
            <Link className={styles.primary} to="/docs/getting-started">
              Open your first coffee shop
            </Link>
            <Link className={styles.secondary} to="/docs/core-concepts/async-initialization">
              How async initialization works
            </Link>
          </div>
        </div>
      </header>

      <main>
        <section className={clsx(styles.container, styles.intro)}>
          <h2>A day in the shop</h2>
          <p>Follow one coffee shop from the code you write to the first cup.</p>
        </section>

        <section className={clsx(styles.container, styles.timeline)} aria-label="A day in the shop">
          <Step>
            <div className={styles.stepText}>
              <span className={styles.eyebrow}>BEFORE OPENING</span>
              <h3>You write the whole shop on one screen.</h3>
              <p className={styles.muted}>
                Registrations are attributes on a static partial class. The espresso machine only implements your
                own <code>IWarmUp</code> interface, so your domain code never references Awaiten.
              </p>
            </div>
            <div className={styles.stepVisual}>
              <pre className={styles.code}>
                <Attr>[Container]</Attr>
                {'\n'}
                <Attr>[AsyncInitializer&lt;IWarmUp&gt;</Attr>(<Kw>nameof</Kw>(IWarmUp.WarmUpAsync))<Attr>]</Attr>
                {'\n'}
                <Attr>[Singleton&lt;EspressoMachine&gt;]</Attr>
                {'\n'}
                <Attr>[Scoped&lt;Order&gt;]</Attr>
                {'\n'}
                <Attr>[Transient&lt;Cup&gt;]</Attr>
                {'\n'}
                <Kw>public static partial class</Kw> <Type>CoffeeShop</Type>;
              </pre>
            </div>
            <div className={styles.install}>
              <PackageInstall package="Awaiten" showLatestVersion={false} />
            </div>
          </Step>

          <Step>
            <div className={styles.stepText}>
              <span className={styles.eyebrow}>BUILD</span>
              <h3>The compiler checks the shop.</h3>
              <p className={styles.muted}>
                If a cup would grab the machine before it is warm, the build stops. So does a missing registration, a
                cycle or a singleton holding on to a scoped service. Other containers find these at startup, or in
                production.
              </p>
              <div className={styles.chips}>
                {diagnostics.map((id) => (
                  <Link key={id} className={styles.chip} to={`/docs/diagnostics#${id.toLowerCase()}`}>
                    {id}
                  </Link>
                ))}
                <Link to="/docs/diagnostics">100+ more →</Link>
              </div>
            </div>
            <div className={styles.stepVisual}>
              <div className={styles.errorCard}>
                <pre className={styles.code}>
                  <Comment>// the cup grabs the machine without waiting for it</Comment>
                  {'\n'}
                  <Kw>public sealed class</Kw> <Type>Cup</Type>(
                  <span className={styles.squiggle}>Func&lt;EspressoMachine&gt;</span> machine);
                </pre>
                <div className={styles.error}>
                  <span className={styles.errorId}>AWT119</span>
                  <span>
                    A synchronous Func relationship targets an async-initialized service. Take{' '}
                    <code>Func&lt;Task&lt;EspressoMachine&gt;&gt;</code> and await it.
                  </span>
                </div>
              </div>
            </div>
          </Step>

          <Step warm>
            <div className={styles.stepText}>
              <span className={clsx(styles.eyebrow, styles.eyebrowWarm)}>OPENING</span>
              <h3>The machine warms up, once.</h3>
              <p className={styles.muted}>
                <code>InitializeAsync</code> runs every async initializer in the graph. The shared espresso machine
                heats its boiler before anyone may use it.
              </p>
            </div>
            <div className={styles.stepVisual}>
              <pre className={styles.code}>
                <Kw>await using</Kw> CoffeeShop.Root shop = <Kw>new</Kw>();
                {'\n'}
                <Kw>await</Kw> shop.InitializeAsync();   <Comment>// the machine warms up</Comment>
              </pre>
              <div className={clsx(styles.service, styles.serviceWarm)}>
                <span className={styles.lifetime}>Singleton · async</span>
                <span className={styles.serviceName}>EspressoMachine</span>
                <span className={styles.muted}>shared by the whole shop</span>
              </div>
            </div>
          </Step>

          <Step last>
            <div className={styles.stepText}>
              <span className={styles.eyebrow}>SERVICE</span>
              <h3>Every cup is ready.</h3>
              <p className={styles.muted}>
                Each order gets its own scope, like a web request. Each cup is made fresh, with a shot from the warm
                machine.
              </p>
            </div>
            <div className={styles.stepVisual}>
              <pre className={styles.code}>
                Cup cup = <Kw>await</Kw> shop.ResolveAsync&lt;Cup&gt;();  <Comment>// a fresh cup, ready</Comment>
              </pre>
              <div className={styles.services}>
                <div className={styles.service}>
                  <span className={styles.lifetime}>Scoped</span>
                  <span className={styles.serviceName}>Order</span>
                </div>
                <div className={styles.service}>
                  <span className={styles.lifetime}>Transient</span>
                  <span className={styles.serviceName}>Cup</span>
                </div>
              </div>
            </div>
          </Step>
        </section>

        <section className={styles.features}>
          <div className={clsx(styles.container, styles.featureGrid)}>
            <div>
              <h3>Domain code stays yours</h3>
              <p className={styles.muted}>
                Registrations and async setup live on the container, so your services never reference Awaiten.
              </p>
            </div>
            <div>
              <h3>Plain C#, AOT clean</h3>
              <p className={styles.muted}>No reflection on any path, so native AOT and trimming just work.</p>
            </div>
            <div>
              <h3>Fits ASP.NET Core</h3>
              <p className={styles.muted}>
                The MS.DI bridge runs your container under the generic host, next to <code>IServiceCollection</code>.
              </p>
            </div>
          </div>
        </section>

        <section className={clsx(styles.container, styles.closing)}>
          <div className={styles.closingText}>
            <h2>Ready to open?</h2>
            <p className={styles.muted}>
              Build the coffee shop end to end, or map your <code>IServiceCollection</code> registrations over.
            </p>
          </div>
          <div className={styles.actions}>
            <Link className={styles.button} to="/docs/getting-started">
              Get started
            </Link>
            <Link className={styles.outline} to="/docs/hosting/migrate-from-msdi">
              Migrate from MS.DI
            </Link>
          </div>
        </section>
      </main>
    </Layout>
  );
}
