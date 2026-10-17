// Meitou UI: window.MeitouUI. React 18 components; styles in bundle.css.
import type { ReactNode, CSSProperties } from 'react';

type Base = { className?: string; style?: CSSProperties };
/** A built-in icon or an image URL. */
export type IconName = 'continue' | 'new' | 'load' | 'options' | 'credits' | 'exit' | (string & {});

export function Window(p: Base & { title: string; meta?: ReactNode; tabs?: ReactNode; footer?: ReactNode; width?: number | string; onClose?: () => void; closable?: boolean; children?: ReactNode }): JSX.Element;
/** Paper card by default; variant "plate" for a dark plate. */
export function Card(p: Base & { title?: ReactNode; variant?: 'paper' | 'plate'; floating?: boolean; children?: ReactNode }): JSX.Element;
export function Message(p: Base & { children: ReactNode }): JSX.Element;
export function Button(p: Base & React.ButtonHTMLAttributes<HTMLButtonElement> & { variant?: 'primary' | 'danger'; size?: 'sm'; icon?: IconName; block?: boolean; pressed?: boolean; children: ReactNode }): JSX.Element;
export function Tabs(p: Base & { tabs: (string | { id: string; label: ReactNode; disabled?: boolean })[]; value?: string; defaultValue?: string; onChange?: (id: string) => void; label?: string }): JSX.Element;
export function Checkbox(p: Base & { label: ReactNode; checked?: boolean; defaultChecked?: boolean; onChange?: (checked: boolean) => void; disabled?: boolean }): JSX.Element;
/** speed: 0 pause, 1 normal, 2 fast, 3 fastest. money in cats. */
export function TimeControls(p: Base & { speed?: 0 | 1 | 2 | 3; defaultSpeed?: 0 | 1 | 2 | 3; onChange?: (n: number) => void; day?: number; time?: string; money?: number }): JSX.Element;
export function StatBar(p: Base & { label: string; value: number; max: number; stun?: number; tone?: 'life' | 'hunger' | 'robot' }): JSX.Element;
/** progress 0..1; bonus signed. */
export function SkillRow(p: Base & { name: string; level: number; bonus?: number; progress?: number }): JSX.Element;
/** health 0..1. */
export function SquadCard(p: Base & { name: string; health?: number; status?: string; selected?: boolean; onClick?: () => void }): JSX.Element;
export type InventoryItem = { id?: string; name: string; short?: string; x: number; y: number; w?: number; h?: number; count?: number; selected?: boolean };
export function InventoryGrid(p: Base & { items: InventoryItem[]; cols?: number; rows?: number; cell?: number; label?: string; onSelect?: (item: InventoryItem, index: number) => void }): JSX.Element;
export function ItemTooltip(p: Base & { name: string; grade?: 0 | 1 | 2 | 3 | 4 | 'artifact'; gradeName?: string; kind?: string; stats?: [string, ReactNode, ('good' | 'bad')?][]; description?: string; weight?: number; value?: number }): JSX.Element;
export function Dialogue(p: Base & { speaker: string; faction?: string; text: ReactNode; choices?: string[]; onChoose?: (index: number, choice: string) => void }): JSX.Element;
/** The main menu's Continue. hotkey null hides the badge. */
export type SaveCardProps = Base & { squad?: string; initial?: string; label?: string; hotkey?: string | null; line?: string; meta?: string; onClick?: () => void };
export function SaveCard(p: SaveCardProps): JSX.Element;
export type MenuItem = { id: string; label: string; icon?: IconName; danger?: boolean };
/** onSelect receives "continue" for the save card. */
export function MainMenu(p: Base & { save?: SaveCardProps | null; items?: MenuItem[]; onSelect?: (id: string) => void; label?: string }): JSX.Element;
